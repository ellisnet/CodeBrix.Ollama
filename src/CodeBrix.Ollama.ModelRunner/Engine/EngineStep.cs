using System;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// What one turn of the decode loop produced: the token the sampler chose, its raw bytes, and whether it
/// ends the generation.
/// </summary>
/// <remarks>
/// The bytes rather than the text, because a token's bytes are not always a whole UTF-8 sequence and the
/// assembling is done off the engine thread - see <see cref="Utf8Assembler"/>. An end-of-generation token
/// carries no bytes: it is a marker, not something to show anyone.
/// </remarks>
internal readonly struct EngineStep
{
    /// <summary>Creates the result of one step.</summary>
    /// <param name="token">The token id.</param>
    /// <param name="bytes">The token's raw bytes.</param>
    /// <param name="isEndOfGeneration">Whether the token ends the generation.</param>
    public EngineStep(int token, byte[] bytes, bool isEndOfGeneration)
    {
        Token = token;
        Bytes = bytes ?? Array.Empty<byte>();
        IsEndOfGeneration = isEndOfGeneration;
    }

    /// <summary>The token the sampler chose.</summary>
    public int Token { get; }

    /// <summary>The token's raw bytes, empty for an end-of-generation token.</summary>
    public byte[] Bytes { get; }

    /// <summary>Whether the token ends the generation.</summary>
    public bool IsEndOfGeneration { get; }
}
