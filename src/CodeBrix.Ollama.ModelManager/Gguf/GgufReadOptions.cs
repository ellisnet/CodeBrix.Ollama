// Ported from Ollama (https://github.com/ollama/ollama), MIT License, Copyright (c) Ollama. Source: fs/gguf/metadata.go at commit a43fad18.
namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The knobs for <see cref="GgufMetadata.ReadAsync(string, GgufReadOptions, System.Threading.CancellationToken)"/>.
/// </summary>
public sealed class GgufReadOptions
{
    /// <summary>
    /// How many elements of an array key-value to retain. An array longer than this is read past and its key is
    /// listed in <see cref="GgufMetadata.OmittedKeys"/> instead. A negative value retains every array. The
    /// default, 1024, keeps a model's token vocabulary out of memory while retaining every ordinary key.
    /// </summary>
    public int MaxArraySize { get; set; } = 1024;

    /// <summary>
    /// Whether to check that every tensor's offset and byte count fall inside the file. The check is skipped
    /// when the source stream cannot report its length.
    /// </summary>
    public bool ValidateTensorData { get; set; } = true;
}
