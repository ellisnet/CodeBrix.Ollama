using System;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Turns the raw bytes of a token stream into text, holding back the tail of an incomplete UTF-8 sequence
/// until the token that finishes it arrives.
/// </summary>
/// <remarks>
/// <para>
/// A token is a run of bytes, not a run of characters. A byte-fallback vocabulary emits one byte at a time,
/// so a single accented letter can span two tokens and an emoji four; decoding each token on its own would
/// put a replacement character in the stream where the character should be. The assembler therefore feeds
/// every byte to one stateful UTF-8 decoder, which emits only the characters that are complete and keeps the
/// rest until the next call.
/// </para>
/// <para>
/// One assembler belongs to one request. <see cref="Flush"/> ends it: anything still incomplete at that
/// point really is malformed and is rendered as a replacement character, which is what the caller wants to
/// see rather than silently losing the bytes.
/// </para>
/// </remarks>
internal sealed class Utf8Assembler
{
    private readonly Decoder decoder = Encoding.UTF8.GetDecoder();

    /// <summary>Adds bytes and returns whatever text they completed.</summary>
    /// <param name="bytes">The bytes, which may be empty.</param>
    /// <returns>The text, which is empty when the bytes only continued a sequence.</returns>
    public string Append(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return string.Empty;

        int count = decoder.GetCharCount(bytes, 0, bytes.Length, false);
        char[] characters = count == 0 ? Array.Empty<char>() : new char[count];
        int written = decoder.GetChars(bytes, 0, bytes.Length, characters, 0, false);

        return written == 0 ? string.Empty : new string(characters, 0, written);
    }

    /// <summary>Ends the stream and returns whatever was still held back.</summary>
    /// <returns>The text, usually empty.</returns>
    public string Flush()
    {
        byte[] nothing = Array.Empty<byte>();

        int count = decoder.GetCharCount(nothing, 0, 0, true);
        char[] characters = count == 0 ? Array.Empty<char>() : new char[count];
        int written = decoder.GetChars(nothing, 0, 0, characters, 0, true);

        decoder.Reset();
        return written == 0 ? string.Empty : new string(characters, 0, written);
    }

    /// <summary>Forgets whatever is held back and starts again.</summary>
    public void Reset()
    {
        decoder.Reset();
    }
}
