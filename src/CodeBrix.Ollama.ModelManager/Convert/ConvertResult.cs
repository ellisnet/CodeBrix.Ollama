using System;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// What a conversion produced: the name it was stored under, how it was read, how much it read and wrote, and
/// which tool recorded itself as having done it.
/// </summary>
public sealed class ConvertResult
{
    internal ConvertResult(string name, CheckpointArchitecture architecture, int tensorCount, long outputBytes,
        long sourceBytes, GgufOutputType typeWritten, string tool, string toolVersion)
    {
        Name = name;
        Architecture = architecture;
        TensorCount = tensorCount;
        OutputBytes = outputBytes;
        SourceBytes = sourceBytes;
        TypeWritten = typeWritten;
        Tool = tool;
        ToolVersion = toolVersion;
    }

    /// <summary>The name the converted model was stored under.</summary>
    public string Name { get; }

    /// <summary>The architecture the checkpoint was read as.</summary>
    public CheckpointArchitecture Architecture { get; }

    /// <summary>How many tensors the GGUF file carries.</summary>
    public int TensorCount { get; }

    /// <summary>The size of the GGUF file in bytes.</summary>
    public long OutputBytes { get; }

    /// <summary>The size of the checkpoint the conversion read, in bytes.</summary>
    public long SourceBytes { get; }

    /// <summary>The numeric type the weights were written as; never <see cref="GgufOutputType.Auto"/>.</summary>
    public GgufOutputType TypeWritten { get; }

    /// <summary>The name of the tool that did the conversion.</summary>
    public string Tool { get; }

    /// <summary>The version of the tool that did the conversion.</summary>
    public string ToolVersion { get; }
}
