namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// What a quantization produced: the name it was stored under, the type it was asked for, how much it read
/// and wrote, and which tool recorded itself as having done it.
/// </summary>
public sealed class QuantizeGgufResult
{
    internal QuantizeGgufResult(string name, string sourceName, string type, long sourceBytes, long outputBytes,
        string tool, string toolVersion)
    {
        Name = name;
        SourceName = sourceName;
        Type = type;
        SourceBytes = sourceBytes;
        OutputBytes = outputBytes;
        Tool = tool;
        ToolVersion = toolVersion;
    }

    /// <summary>The name the quantized model was stored under.</summary>
    public string Name { get; }

    /// <summary>The name of the model it was quantized from, spelled as the store spells it.</summary>
    public string SourceName { get; }

    /// <summary>The quantization, in the spelling the caller gave it.</summary>
    public string Type { get; }

    /// <summary>The size of the GGUF file that was quantized, in bytes.</summary>
    public long SourceBytes { get; }

    /// <summary>The size of the quantized GGUF file, in bytes.</summary>
    public long OutputBytes { get; }

    /// <summary>The name of the tool that did the quantizing, or <see langword="null"/>.</summary>
    public string Tool { get; }

    /// <summary>The version of the tool that did the quantizing, or <see langword="null"/>.</summary>
    public string ToolVersion { get; }
}
