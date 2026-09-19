using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager; //was previously: conversion/base.py@b10221;

/// <summary>
/// A checkpoint's weights, however many files they are held in: one <c>model.safetensors</c>, one
/// <c>pytorch_model.bin</c>, or a set of shards named by a <c>*.index.json</c> beside them. It is one
/// <see cref="ICheckpointReader"/> over the parts, and it lists the tensors in the order the inference engine's
/// own converter visits them.
/// </summary>
/// <remarks>
/// <para>
/// THE ORDER IS THE POINT, because it is the order the tensors are written to the GGUF file in. The engine
/// walks the parts in sorted file-name order and, inside each part, takes a safetensors container in tensor-NAME
/// order and a zip pickle in the order its state dictionary was written. For a split checkpoint that is not the
/// same as sorting the whole set by name, and it is not the same as the order the shards were written in
/// either - a tensor's place depends on which shard it landed in.
/// </para>
/// <para>
/// The shards are opened one at a time and held open only for as long as the conversion runs; nothing here
/// reads a tensor's values. A part contributes its tensors to one flat list, and each tensor remembers which
/// part it came from, so <see cref="OpenTensorAsync"/> is a lookup rather than a search.
/// </para>
/// </remarks>
internal sealed class CheckpointWeights : ICheckpointReader
{
    /// <summary>The index file a sharded safetensors checkpoint carries.</summary>
    internal const string SafetensorsIndexFileName = "model.safetensors.index.json";

    /// <summary>The index file a sharded PyTorch zip-pickle checkpoint carries.</summary>
    internal const string PickleIndexFileName = "pytorch_model.bin.index.json";

    private readonly List<ICheckpointReader> _readers;
    private readonly Dictionary<CheckpointTensor, ICheckpointReader> _owners;

    private CheckpointWeights(List<ICheckpointReader> readers, List<CheckpointTensor> tensors,
        Dictionary<CheckpointTensor, ICheckpointReader> owners, IReadOnlyList<string> partPaths,
        bool isSafetensors, long totalBytes)
    {
        _readers = readers;
        _owners = owners;
        Tensors = tensors;
        PartPaths = partPaths;
        IsSafetensors = isSafetensors;
        TotalBytes = totalBytes;
    }

    /// <summary>The tensors of every part, in the order the engine's converter visits them.</summary>
    public IReadOnlyList<CheckpointTensor> Tensors { get; }

    /// <summary>The weight files this reads, in the order they are read.</summary>
    internal IReadOnlyList<string> PartPaths { get; }

    /// <summary>Whether the parts are safetensors containers rather than PyTorch zip pickles.</summary>
    internal bool IsSafetensors { get; }

    /// <summary>The size of every weight file added together.</summary>
    internal long TotalBytes { get; }

    /// <summary>Opens the weights a checkpoint directory holds.</summary>
    /// <param name="directory">The checkpoint directory.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    /// <returns>The reader. The caller disposes it.</returns>
    /// <exception cref="CheckpointFormatException">
    /// The directory holds no weights, or an index and the files beside it do not agree.
    /// </exception>
    internal static async Task<CheckpointWeights> OpenAsync(string directory,
        CancellationToken cancellationToken)
    {
        if (directory == null)
        {
            throw new ArgumentNullException(nameof(directory));
        }

        List<string> partNames = FindPartNames(directory, "model", ".safetensors");
        bool isSafetensors = partNames.Count > 0;
        if (!isSafetensors)
        {
            partNames = FindPartNames(directory, "pytorch_model", ".bin");
        }

        CheckpointShardIndex index = await CheckpointShardIndex.LoadAsync(directory,
            isSafetensors ? SafetensorsIndexFileName : PickleIndexFileName, cancellationToken)
            .ConfigureAwait(false);
        if (index != null)
        {
            // The index decides which files are parts: one sitting in the folder that it does not name is not
            // opened, exactly as the engine's converter leaves it alone.
            partNames = new List<string>(index.ShardFileNames);
        }

        if (partNames.Count == 0)
        {
            throw new CheckpointFormatException("The folder \"" + directory + "\" holds neither a " +
                "model.safetensors nor a pytorch_model.bin, so there are no weights to convert.");
        }

        var readers = new List<ICheckpointReader>();
        var tensors = new List<CheckpointTensor>();
        var owners = new Dictionary<CheckpointTensor, ICheckpointReader>();
        var paths = new List<string>();
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        long totalBytes = 0;
        try
        {
            for (int i = 0; i < partNames.Count; i++)
            {
                string path = Path.Combine(directory, partNames[i]);
                RequirePart(path, partNames[i], index);
                totalBytes += new FileInfo(path).Length;
                ICheckpointReader reader = isSafetensors
                    ? await SafetensorsReader.OpenAsync(path, cancellationToken).ConfigureAwait(false)
                    : await PyTorchZipReader.OpenAsync(path, cancellationToken).ConfigureAwait(false);
                readers.Add(reader);
                paths.Add(path);
                AddPart(reader, partNames[i], isSafetensors, index, tensors, owners, sources);
            }

            RequireEveryIndexedTensor(index, sources);
        }
        catch
        {
            for (int i = readers.Count - 1; i >= 0; i--)
            {
                readers[i].Dispose();
            }

            throw;
        }

        return new CheckpointWeights(readers, tensors, owners, paths, isSafetensors, totalBytes);
    }

    /// <summary>Opens a stream over one tensor's values, in whichever part holds it.</summary>
    /// <param name="tensor">A descriptor this reader returned.</param>
    /// <param name="cancellationToken">A token that cancels the open.</param>
    /// <returns>A stream of exactly the tensor's bytes.</returns>
    public Task<Stream> OpenTensorAsync(CheckpointTensor tensor, CancellationToken cancellationToken = default)
    {
        if (tensor == null)
        {
            throw new ArgumentNullException(nameof(tensor));
        }

        if (!_owners.TryGetValue(tensor, out ICheckpointReader reader))
        {
            throw new ArgumentException("The tensor \"" + tensor.Name + "\" is not one of this checkpoint's.",
                nameof(tensor));
        }

        return reader.OpenTensorAsync(tensor, cancellationToken);
    }

    /// <summary>Releases every part.</summary>
    public void Dispose()
    {
        for (int i = _readers.Count - 1; i >= 0; i--)
        {
            _readers[i].Dispose();
        }

        _readers.Clear();
    }

    private static void AddPart(ICheckpointReader reader, string partName, bool isSafetensors,
        CheckpointShardIndex index, List<CheckpointTensor> tensors,
        Dictionary<CheckpointTensor, ICheckpointReader> owners, Dictionary<string, string> sources)
    {
        var ordered = new List<CheckpointTensor>(reader.Tensors);
        if (isSafetensors)
        {
            // The engine reads a safetensors container in tensor-name order, which is what the safetensors
            // library itself does; a zip pickle keeps the order its state dictionary was written in.
            ordered.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
        }

        for (int i = 0; i < ordered.Count; i++)
        {
            CheckpointTensor tensor = ordered[i];
            if (sources.TryGetValue(tensor.Name, out string first))
            {
                throw new CheckpointFormatException("The tensor \"" + tensor.Name + "\" is in \"" + first +
                    "\" and in \"" + partName + "\", so which of the two the model uses is not decided.");
            }

            if (index != null && !index.TryGetShard(tensor.Name, out _))
            {
                throw new CheckpointFormatException("The file \"" + partName + "\" holds the tensor \"" +
                    tensor.Name + "\", which \"" + Path.GetFileName(index.Path) + "\" does not list.");
            }

            sources[tensor.Name] = partName;
            owners[tensor] = reader;
            tensors.Add(tensor);
        }
    }

    private static void RequirePart(string path, string partName, CheckpointShardIndex index)
    {
        if (File.Exists(path))
        {
            return;
        }

        if (index != null)
        {
            throw new CheckpointFormatException("The file \"" + Path.GetFileName(index.Path) + "\" names the " +
                "shard \"" + partName + "\", which is not in the checkpoint.");
        }

        throw new CheckpointFormatException("The weight file \"" + path + "\" is not there.");
    }

    private static void RequireEveryIndexedTensor(CheckpointShardIndex index, Dictionary<string, string> sources)
    {
        if (index == null)
        {
            return;
        }

        for (int i = 0; i < index.TensorNames.Count; i++)
        {
            string name = index.TensorNames[i];
            if (!sources.ContainsKey(name))
            {
                index.TryGetShard(name, out string shard);
                throw new CheckpointFormatException("The file \"" + Path.GetFileName(index.Path) +
                    "\" lists the tensor \"" + name + "\" in \"" + shard + "\", which does not hold it.");
            }
        }
    }

    private static List<string> FindPartNames(string directory, string prefix, string suffix)
    {
        var parts = new List<string>();
        if (!Directory.Exists(directory))
        {
            return parts;
        }

        foreach (string path in Directory.GetFiles(directory))
        {
            string name = Path.GetFileName(path);
            if (name.StartsWith(prefix, StringComparison.Ordinal)
                && name.EndsWith(suffix, StringComparison.Ordinal))
            {
                parts.Add(name);
            }
        }

        parts.Sort(StringComparer.Ordinal);
        return parts;
    }
}
