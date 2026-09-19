using System;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// What one quantization did: which file it read, which file it wrote, the type it wrote and how long the
/// engine took over it.
/// </summary>
public sealed class QuantizeResult
{
    /// <summary>The full path of the file that was read.</summary>
    public string InputPath { get; init; } = "";

    /// <summary>The full path of the file that was written.</summary>
    public string OutputPath { get; init; } = "";

    /// <summary>The type that was asked for and written.</summary>
    public GgufQuantizationType Type { get; init; }

    /// <summary>The size of the file that was read, in bytes.</summary>
    public long InputBytes { get; init; }

    /// <summary>The size of the file that was written, in bytes.</summary>
    public long OutputBytes { get; init; }

    /// <summary>
    /// How long the engine spent quantizing - the native call alone, without the argument checks around it
    /// or the move that puts the finished file in place.
    /// </summary>
    public TimeSpan Elapsed { get; init; }
}
