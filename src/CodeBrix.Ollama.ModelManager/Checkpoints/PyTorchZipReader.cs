using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Reads a PyTorch <c>.bin</c> checkpoint: a zip archive holding <c>&lt;archive&gt;/data.pkl</c>, one file per
/// storage under <c>&lt;archive&gt;/data/</c>, and small sidecars such as <c>version</c> and <c>byteorder</c>.
/// </summary>
/// <remarks>
/// <para>
/// The pickle is interpreted by <see cref="RestrictedUnpickler"/>, which never imports or evaluates anything;
/// this reader adds the checks that need the archive: that the storage a tensor names is really in the file,
/// that the tensor's strides describe a contiguous run the reader can stream, that the run is inside its
/// storage, and that the values were written little endian.
/// </para>
/// <para>
/// The storage entries are stored rather than deflated, so a tensor's bytes are streamed straight out of the
/// archive: nothing is decompressed and no checkpoint is ever held in memory.
/// </para>
/// </remarks>
internal sealed class PyTorchZipReader : ICheckpointReader
{
    /// <summary>The largest <c>data.pkl</c> entry the reader accepts.</summary>
    internal const long MaxPickleLength = 256L << 20;

    private readonly string _path;
    private readonly ZipArchive _archive;
    private readonly Dictionary<string, string> _entryNames;

    private PyTorchZipReader(string path, ZipArchive archive, IReadOnlyList<CheckpointTensor> tensors,
        Dictionary<string, string> entryNames, string archivePrefix)
    {
        _path = path;
        _archive = archive;
        _entryNames = entryNames;
        Tensors = tensors;
        ArchivePrefix = archivePrefix;
    }

    /// <summary>The tensors the pickle names, in the order it names them.</summary>
    public IReadOnlyList<CheckpointTensor> Tensors { get; }

    /// <summary>The folder every entry sits under, for example <c>pytorch_model/</c>. It varies by publisher.</summary>
    internal string ArchivePrefix { get; }

    /// <summary>Opens a PyTorch zip checkpoint and interprets its pickle.</summary>
    /// <param name="path">The file to open.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The reader.</returns>
    internal static async Task<PyTorchZipReader> OpenAsync(string path, CancellationToken cancellationToken)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16,
            FileOptions.Asynchronous | FileOptions.RandomAccess);
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        }
        catch (InvalidDataException exception)
        {
            stream.Dispose();
            throw new CheckpointFormatException(
                "The checkpoint \"" + path + "\" is not a zip archive, so it is not a PyTorch checkpoint.",
                exception);
        }

        try
        {
            return await ReadAsync(path, archive, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }

    /// <summary>Opens a stream over one tensor's values.</summary>
    /// <param name="tensor">A descriptor this reader returned.</param>
    /// <param name="cancellationToken">A token that cancels the open.</param>
    /// <returns>A stream of exactly the tensor's bytes.</returns>
    public async Task<Stream> OpenTensorAsync(CheckpointTensor tensor,
        CancellationToken cancellationToken = default)
    {
        if (tensor == null)
        {
            throw new ArgumentNullException(nameof(tensor));
        }

        if (!_entryNames.TryGetValue(tensor.Name, out string entryName))
        {
            throw new CheckpointFormatException(
                "The checkpoint \"" + _path + "\" was not asked for tensor \"" + tensor.Name + "\".");
        }

        ZipArchiveEntry entry = _archive.GetEntry(entryName);
        if (entry == null)
        {
            throw new CheckpointFormatException("The checkpoint \"" + _path + "\" names a storage in \"" +
                entryName + "\", which the archive does not hold.");
        }

        Stream entryStream = entry.Open();
        try
        {
            await SkipAsync(entryStream, tensor.ByteOffset, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            entryStream.Dispose();
            throw;
        }

        return new CheckpointRegionStream(entryStream, tensor.ByteCount);
    }

    /// <summary>Closes the archive and the file underneath it.</summary>
    public void Dispose()
    {
        _archive.Dispose();
    }

    private static async Task<PyTorchZipReader> ReadAsync(string path, ZipArchive archive,
        CancellationToken cancellationToken)
    {
        ZipArchiveEntry pickleEntry = FindPickle(path, archive);
        string prefix = pickleEntry.FullName.Substring(0, pickleEntry.FullName.Length - "data.pkl".Length);
        RequireLittleEndian(path, archive, prefix, cancellationToken);

        if (pickleEntry.Length > MaxPickleLength)
        {
            throw new CheckpointFormatException("The checkpoint \"" + path + "\" holds a pickle of " +
                pickleEntry.Length.ToString(CultureInfo.InvariantCulture) + " bytes, more than the maximum of " +
                MaxPickleLength.ToString(CultureInfo.InvariantCulture) + ".");
        }

        var pickle = new byte[pickleEntry.Length];
        using (Stream pickleStream = pickleEntry.Open())
        {
            await pickleStream.ReadExactlyAsync(pickle, cancellationToken).ConfigureAwait(false);
        }

        object root = RestrictedUnpickler.Load(pickle);
        var state = root as OrderedDictionary<string, object>;
        if (state == null)
        {
            throw new CheckpointFormatException("The pickle in \"" + path + "\" does not describe a dictionary " +
                "of tensors; a PyTorch checkpoint this reader can use is a state dictionary.");
        }

        var tensors = new List<CheckpointTensor>();
        var entryNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, object> entry in state)
        {
            var tensor = entry.Value as PickleTensor;
            if (tensor == null)
            {
                throw new CheckpointFormatException("The pickle in \"" + path + "\" maps \"" + entry.Key +
                    "\" on to something that is not a tensor.");
            }

            string storageEntry = prefix + "data/" + tensor.Storage.Key;
            ZipArchiveEntry storage = archive.GetEntry(storageEntry);
            if (storage == null)
            {
                throw new CheckpointFormatException("The pickle in \"" + path + "\" puts \"" + entry.Key +
                    "\" in the storage \"" + storageEntry + "\", which the archive does not hold.");
            }

            tensors.Add(Describe(path, entry.Key, tensor, storage.Length));
            entryNames[entry.Key] = storageEntry;
        }

        return new PyTorchZipReader(path, archive, tensors, entryNames, prefix);
    }

    private static CheckpointTensor Describe(string path, string name, PickleTensor tensor, long storageBytes)
    {
        int width = CheckpointDataTypes.GetByteWidth(tensor.Storage.DataType);
        if (tensor.Shape.Count != tensor.Strides.Count)
        {
            throw new CheckpointFormatException("Tensor \"" + name + "\" in \"" + path + "\" has " +
                tensor.Shape.Count + " dimensions but " + tensor.Strides.Count + " strides.");
        }

        long expected = 1;
        for (int i = tensor.Shape.Count - 1; i >= 0; i--)
        {
            if (tensor.Strides[i] != expected && tensor.Shape[i] != 1)
            {
                throw new CheckpointFormatException("Tensor \"" + name + "\" in \"" + path + "\" is not " +
                    "contiguous: dimension " + i + " has stride " + tensor.Strides[i] + " where a contiguous " +
                    "tensor would have " + expected + ". This reader streams tensors and does not re-lay them out.");
            }

            expected *= tensor.Shape[i];
        }

        var descriptor = new CheckpointTensor(name, tensor.Storage.DataType, tensor.Shape,
            tensor.StorageOffset * width);
        long end = tensor.StorageOffset + descriptor.ElementCount;
        if (end > tensor.Storage.ElementCount)
        {
            throw new CheckpointFormatException("Tensor \"" + name + "\" in \"" + path + "\" ends at element " +
                end + " of a storage that holds " + tensor.Storage.ElementCount + ".");
        }

        if (descriptor.ByteOffset + descriptor.ByteCount > storageBytes)
        {
            throw new CheckpointFormatException("Tensor \"" + name + "\" in \"" + path + "\" ends at byte " +
                (descriptor.ByteOffset + descriptor.ByteCount) + " of a storage file that is " + storageBytes +
                " bytes long.");
        }

        return descriptor;
    }

    private static ZipArchiveEntry FindPickle(string path, ZipArchive archive)
    {
        ZipArchiveEntry found = null;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (!entry.FullName.EndsWith("/data.pkl", StringComparison.Ordinal))
            {
                continue;
            }

            if (found != null)
            {
                throw new CheckpointFormatException("The checkpoint \"" + path + "\" holds more than one " +
                    "data.pkl entry; this reader does not guess which one is the model.");
            }

            found = entry;
        }

        if (found == null)
        {
            throw new CheckpointFormatException("The checkpoint \"" + path + "\" holds no data.pkl entry, so " +
                "it is not a PyTorch checkpoint this reader can read.");
        }

        return found;
    }

    private static void RequireLittleEndian(string path, ZipArchive archive, string prefix,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ZipArchiveEntry entry = archive.GetEntry(prefix + "byteorder");
        if (entry == null)
        {
            return;
        }

        string value;
        using (Stream stream = entry.Open())
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            value = reader.ReadToEnd().Trim();
        }

        if (!string.Equals(value, "little", StringComparison.Ordinal))
        {
            throw new CheckpointFormatException("The checkpoint \"" + path + "\" was written on a \"" + value +
                "\" endian machine. This reader reads little-endian checkpoints only.");
        }
    }

    private static async Task SkipAsync(Stream stream, long count, CancellationToken cancellationToken)
    {
        if (count <= 0)
        {
            return;
        }

        var buffer = new byte[Math.Min(count, 1 << 16)];
        while (count > 0)
        {
            int take = (int)Math.Min(count, buffer.Length);
            int read = await stream.ReadAsync(new Memory<byte>(buffer, 0, take), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                throw new CheckpointFormatException(
                    "A storage entry ends before the tensor that lives inside it begins.");
            }

            count -= read;
        }
    }
}
