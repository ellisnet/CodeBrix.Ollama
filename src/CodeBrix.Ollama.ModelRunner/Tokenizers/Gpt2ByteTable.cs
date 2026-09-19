using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner; //was previously: src/transformers/models/gpt2/tokenization_gpt2.py@v4.57.6

/// <summary>
/// The byte-to-symbol table a byte-level byte-pair encoding is written in: one printable character for every
/// one of the 256 byte values, so that a merge table can be expressed as ordinary text.
/// </summary>
/// <remarks>
/// <para>
/// A byte-level vocabulary has to name every byte, and a merge file is a text file whose columns are
/// separated by spaces and whose rows are separated by newlines - so a space, a newline and every control
/// byte need a printable stand-in before they can be written down at all. The table used by every tokenizer
/// of this family gives the 188 bytes that are already printable themselves, and maps the remaining 68 to the
/// unused block beginning at U+0100: a space becomes U+0120, a newline U+010A, a tab U+0109.
/// </para>
/// <para>
/// The table is DERIVED here rather than written out, in exactly the steps the published code takes, because
/// the derivation is what makes it checkable: the printable runs are stated once and the order the remaining
/// bytes are allotted in follows from them.
/// </para>
/// </remarks>
internal static class Gpt2ByteTable
{
    private static readonly char[] Symbols = BuildSymbols();
    private static readonly Dictionary<char, byte> Bytes = BuildBytes(Symbols);

    /// <summary>The symbol that stands for one byte value.</summary>
    /// <param name="value">The byte.</param>
    /// <returns>Its symbol.</returns>
    internal static char Symbol(byte value) => Symbols[value];

    /// <summary>The byte a symbol stands for.</summary>
    /// <param name="symbol">The symbol.</param>
    /// <param name="value">The byte, when the symbol is one of the 256.</param>
    /// <returns><see langword="true"/> when the symbol is one of the 256.</returns>
    internal static bool TryByte(char symbol, out byte value) => Bytes.TryGetValue(symbol, out value);

    /// <summary>Turns a run of text into the symbols of its UTF-8 bytes.</summary>
    /// <param name="utf8">The bytes.</param>
    /// <param name="count">How many of them to take.</param>
    /// <returns>The symbols, one character per byte.</returns>
    internal static string Encode(byte[] utf8, int count)
    {
        char[] symbols = new char[count];
        for (int i = 0; i < count; i++) symbols[i] = Symbols[utf8[i]];
        return new string(symbols);
    }

    private static char[] BuildSymbols()
    {
        //The bytes that stand for themselves: '!' to '~', the Latin-1 punctuation run and the accented run.
        //Everything else - the controls, the space, the delete character, the Latin-1 control block and the
        //soft hyphen - is allotted a symbol from U+0100 upwards in ascending byte order.
        bool[] printable = new bool[256];
        for (int b = '!'; b <= '~'; b++) printable[b] = true;
        for (int b = 0xA1; b <= 0xAC; b++) printable[b] = true;
        for (int b = 0xAE; b <= 0xFF; b++) printable[b] = true;

        char[] symbols = new char[256];
        int next = 0;
        for (int b = 0; b < 256; b++)
        {
            if (printable[b])
            {
                symbols[b] = (char)b;
                continue;
            }

            symbols[b] = (char)(256 + next);
            next++;
        }

        return symbols;
    }

    private static Dictionary<char, byte> BuildBytes(char[] symbols)
    {
        Dictionary<char, byte> bytes = new Dictionary<char, byte>(symbols.Length);
        for (int b = 0; b < symbols.Length; b++) bytes[symbols[b]] = (byte)b;
        return bytes;
    }
}
