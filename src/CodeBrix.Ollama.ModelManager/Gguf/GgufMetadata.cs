// Ported from Ollama (https://github.com/ollama/ollama), MIT License, Copyright (c) Ollama. Source: fs/gguf/metadata.go at commit a43fad18.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The metadata of one GGUF file: key-values and tensor descriptors, never tensor data.
/// </summary>
public sealed class GgufMetadata
{
    private static readonly IReadOnlyList<GgufTensorInfo> NoTensors = Array.Empty<GgufTensorInfo>();

    private readonly OrderedDictionary<string, GgufValue> _keyValues;
    private readonly List<string> _keys;
    private readonly List<GgufTensorInfo> _tensors;
    private readonly List<string> _omittedKeys;

    internal GgufMetadata(uint version, bool isBigEndian, long alignment, long tensorDataOffset, long fileSize,
        OrderedDictionary<string, GgufValue> keyValues, List<string> keys, List<GgufTensorInfo> tensors,
        List<string> omittedKeys, ulong parameterCount, ulong tensorDataSize)
    {
        Version = version;
        IsBigEndian = isBigEndian;
        Alignment = alignment;
        TensorDataOffset = tensorDataOffset;
        FileSize = fileSize;
        ParameterCount = parameterCount;
        TensorDataSize = tensorDataSize;
        _keyValues = keyValues;
        _keys = keys;
        _tensors = tensors;
        _omittedKeys = omittedKeys;
    }

    /// <summary>Reads the metadata of the GGUF file at a path.</summary>
    /// <param name="path">The path of the file to read.</param>
    /// <param name="options">The read options, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The metadata of the file.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty.</exception>
    /// <exception cref="GgufFormatException">The file is not a GGUF file this reader can interpret.</exception>
    public static async Task<GgufMetadata> ReadAsync(string path, GgufReadOptions options = null,
        CancellationToken cancellationToken = default)
    {
        if (path == null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        if (path.Length == 0)
        {
            throw new ArgumentException("The path must not be empty.", nameof(path));
        }

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using (stream.ConfigureAwait(false))
        {
            return await GgufReader.ReadAsync(stream, options, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Reads the metadata of a GGUF file from a stream positioned at its first byte.</summary>
    /// <remarks>
    /// The stream is read forward only, through the reader's own buffer. A seekable stream also supplies the
    /// file size that the tensor ranges are checked against; when the stream cannot report a length, that
    /// check is skipped.
    /// </remarks>
    /// <param name="stream">The stream to read.</param>
    /// <param name="options">The read options, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The metadata of the file.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="GgufFormatException">The stream is not a GGUF file this reader can interpret.</exception>
    public static Task<GgufMetadata> ReadAsync(Stream stream, GgufReadOptions options = null,
        CancellationToken cancellationToken = default)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        return GgufReader.ReadAsync(stream, options, cancellationToken);
    }

    /// <summary>The GGUF format version the file declares.</summary>
    public uint Version { get; }

    /// <summary><see langword="true"/> when the file is big-endian, that is when its magic reads <c>FUGG</c>.</summary>
    public bool IsBigEndian { get; }

    /// <summary>The <c>general.alignment</c> value, or 32 when the file does not carry one.</summary>
    public long Alignment { get; }

    /// <summary>The aligned byte offset at which the tensor data section begins.</summary>
    public long TensorDataOffset { get; }

    /// <summary>The size of the file in bytes, or -1 when the source could not report it.</summary>
    public long FileSize { get; }

    /// <summary>The sum of every tensor's element count.</summary>
    public ulong ParameterCount { get; }

    /// <summary>The sum of every tensor's byte count.</summary>
    public ulong TensorDataSize { get; }

    /// <summary>Every key-value in the file, keyed by its exact key and enumerated in file order.</summary>
    public IReadOnlyDictionary<string, GgufValue> KeyValues
    {
        get { return _keyValues; }
    }

    /// <summary>Every key in the order the file stores it.</summary>
    public IReadOnlyList<string> Keys
    {
        get { return _keys; }
    }

    /// <summary>Every tensor descriptor in the order the file stores it.</summary>
    public IReadOnlyList<GgufTensorInfo> Tensors
    {
        get { return _tensors; }
    }

    /// <summary>
    /// The keys of the array values that were longer than <see cref="GgufReadOptions.MaxArraySize"/> and were
    /// therefore read past instead of retained.
    /// </summary>
    public IReadOnlyList<string> OmittedKeys
    {
        get { return _omittedKeys; }
    }

    /// <summary>The <c>general.architecture</c> value, or <c>"unknown"</c> when the file has none.</summary>
    public string Architecture
    {
        get { return GetString("general.architecture", "unknown"); }
    }

    /// <summary>The <c>general.type</c> value, or <c>"unknown"</c> when the file has none.</summary>
    public string Kind
    {
        get { return GetString("general.type", "unknown"); }
    }

    /// <summary>The <c>general.file_type</c> value, or <see cref="GgufFileType.Unknown"/> when it is absent.</summary>
    public GgufFileType FileType
    {
        get
        {
            ulong value = GetUInt64("general.file_type", (ulong)GgufFileType.Unknown);
            if (value > uint.MaxValue)
            {
                return GgufFileType.Unknown;
            }

            return (GgufFileType)value;
        }
    }

    /// <summary>The name of <see cref="FileType"/>, for example <c>"Q4_K_M"</c>.</summary>
    public string FileTypeName
    {
        get { return GgufFileTypes.GetName(FileType); }
    }

    /// <summary>The architecture's <c>block_count</c>, or 0 when it is absent.</summary>
    public ulong BlockCount
    {
        get { return GetUInt64("block_count"); }
    }

    /// <summary>The architecture's <c>embedding_length</c>, or 0 when it is absent.</summary>
    public ulong EmbeddingLength
    {
        get { return GetUInt64("embedding_length"); }
    }

    /// <summary>The architecture's <c>context_length</c>, or 0 when it is absent.</summary>
    public ulong ContextLength
    {
        get { return GetUInt64("context_length"); }
    }

    /// <summary>The <c>tokenizer.chat_template</c> value, or <see langword="null"/> when the file has none.</summary>
    public string ChatTemplate
    {
        get { return GetString("tokenizer.chat_template"); }
    }

    /// <summary>
    /// The largest <c>attention.head_count</c> the file declares, taking the maximum when the value is an
    /// array of per-layer counts. It is 1 when the key is absent.
    /// </summary>
    public ulong HeadCountMax
    {
        get
        {
            GetUIntRange("attention.head_count", 1, out ulong _, out ulong maximum);
            return maximum;
        }
    }

    /// <summary>
    /// The smallest <c>attention.head_count_kv</c> the file declares, taking the minimum when the value is an
    /// array of per-layer counts. It is 1 when the key is absent.
    /// </summary>
    public ulong HeadCountKvMin
    {
        get
        {
            GetUIntRange("attention.head_count_kv", 1, out ulong minimum, out ulong _);
            return minimum;
        }
    }

    /// <summary>Reports whether the file carries a key, qualifying it by architecture the way <see cref="GetValue"/> does.</summary>
    /// <param name="key">The key to look for.</param>
    /// <returns><see langword="true"/> when the key is present.</returns>
    public bool Has(string key)
    {
        return GetValue(key) != null;
    }

    /// <summary>
    /// Looks a key up, qualifying it by architecture first. A key that already starts with <c>general.</c> or
    /// <c>tokenizer.</c> is used as written; a key that starts with <c>split.</c> is tried as written before
    /// being qualified; every other key is looked up as <c>&lt;architecture&gt;.&lt;key&gt;</c>.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <returns>The value, or <see langword="null"/> when the file has no such key.</returns>
    public GgufValue GetValue(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        if (key.StartsWith("split.", StringComparison.Ordinal))
        {
            GgufValue exact = GetExactValue(key);
            if (exact != null)
            {
                return exact;
            }
        }

        if (!key.StartsWith("general.", StringComparison.Ordinal) &&
            !key.StartsWith("tokenizer.", StringComparison.Ordinal))
        {
            key = Architecture + "." + key;
        }

        return GetExactValue(key);
    }

    /// <summary>Looks a key up exactly as written, without architecture qualification.</summary>
    /// <param name="key">The key to look up.</param>
    /// <returns>The value, or <see langword="null"/> when the file has no such key.</returns>
    public GgufValue GetExactValue(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        return _keyValues.TryGetValue(key, out GgufValue value) ? value : null;
    }

    /// <summary>Reads a key as a string.</summary>
    /// <param name="key">The key to look up, qualified the way <see cref="GetValue"/> qualifies it.</param>
    /// <param name="defaultValue">What to return when the key is absent.</param>
    /// <returns>The string, an empty string when the value is not a string, or <paramref name="defaultValue"/>.</returns>
    public string GetString(string key, string defaultValue = null)
    {
        GgufValue value = GetValue(key);
        if (value == null)
        {
            return defaultValue;
        }

        return value.AsString();
    }

    /// <summary>Reads a key as an unsigned integer, accepting a non-negative signed value as well.</summary>
    /// <param name="key">The key to look up, qualified the way <see cref="GetValue"/> qualifies it.</param>
    /// <param name="defaultValue">What to return when the key is absent or is not an integer.</param>
    /// <returns>The value, or <paramref name="defaultValue"/>.</returns>
    public ulong GetUInt64(string key, ulong defaultValue = 0)
    {
        GgufValue value = GetValue(key);
        if (value == null)
        {
            return defaultValue;
        }

        if (value.TryGetUInt64(out ulong unsignedValue))
        {
            return unsignedValue;
        }

        if (value.TryGetInt64(out long signedValue) && signedValue >= 0)
        {
            return (ulong)signedValue;
        }

        return defaultValue;
    }

    /// <summary>Reads a key as a boolean.</summary>
    /// <param name="key">The key to look up, qualified the way <see cref="GetValue"/> qualifies it.</param>
    /// <param name="defaultValue">What to return when the key is absent or is not a boolean.</param>
    /// <returns>The value, or <paramref name="defaultValue"/>.</returns>
    public bool GetBool(string key, bool defaultValue = false)
    {
        GgufValue value = GetValue(key);
        if (value != null && value.RawValue is bool booleanValue)
        {
            return booleanValue;
        }

        return defaultValue;
    }

    /// <summary>Finds a tensor descriptor by its exact name.</summary>
    /// <param name="name">The tensor name.</param>
    /// <returns>The descriptor, or <see langword="null"/> when the file has no such tensor.</returns>
    public GgufTensorInfo GetTensor(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        for (int i = 0; i < _tensors.Count; i++)
        {
            if (string.Equals(_tensors[i].Name, name, StringComparison.Ordinal))
            {
                return _tensors[i];
            }
        }

        return null;
    }

    /// <summary>Returns every tensor descriptor whose name starts with a prefix, in file order.</summary>
    /// <param name="prefix">The prefix to match. An empty or <see langword="null"/> prefix returns every tensor.</param>
    /// <returns>The matching descriptors.</returns>
    public IReadOnlyList<GgufTensorInfo> GetTensors(string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return _tensors;
        }

        List<GgufTensorInfo> matches = null;
        for (int i = 0; i < _tensors.Count; i++)
        {
            if (_tensors[i].Name != null && _tensors[i].Name.StartsWith(prefix, StringComparison.Ordinal))
            {
                matches ??= new List<GgufTensorInfo>();
                matches.Add(_tensors[i]);
            }
        }

        return matches ?? NoTensors;
    }

    private void GetUIntRange(string key, ulong defaultValue, out ulong minimum, out ulong maximum)
    {
        GgufValue value = GetValue(key);
        ulong[] values = value?.AsUInt64Array();
        if (values == null || values.Length == 0)
        {
            long[] signedValues = value?.AsInt64Array();
            if (signedValues != null && signedValues.Length > 0)
            {
                var converted = new ulong[signedValues.Length];
                for (int i = 0; i < signedValues.Length; i++)
                {
                    if (signedValues[i] < 0)
                    {
                        minimum = defaultValue;
                        maximum = defaultValue;
                        return;
                    }

                    converted[i] = (ulong)signedValues[i];
                }

                values = converted;
            }
            else
            {
                values = null;
            }
        }

        if (values == null || values.Length == 0)
        {
            ulong single = GetUInt64(key, defaultValue);
            minimum = single;
            maximum = single;
            return;
        }

        minimum = values[0];
        maximum = values[0];
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] < minimum)
            {
                minimum = values[i];
            }

            if (values[i] > maximum)
            {
                maximum = values[i];
            }
        }
    }
}
