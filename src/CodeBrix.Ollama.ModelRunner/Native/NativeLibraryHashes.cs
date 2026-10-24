using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The SHA-256 of every native library this package ships, keyed by runtime identifier and file name, read
/// from the <c>Native/SHA256SUMS</c> table compiled into this assembly.
/// </summary>
/// <remarks>
/// <para>
/// The table is the output of <c>sha256sum */native/*codebrix_llama.*</c> run inside the project's
/// <c>runtimes/</c> folder: one line per native, <c>&lt;sha256&gt;  &lt;rid&gt;/native/&lt;file&gt;</c>, so
/// <c>sha256sum -c ../Native/SHA256SUMS</c> run in the same place checks it. It is regenerated whenever a
/// native is adopted (llama-native-tools/README.txt, ADOPTING A BUILD), and a test fails the suite when a
/// shipped native and its line disagree.
/// </para>
/// <para>
/// The loader hashes a candidate file before it asks the operating system to load it, and refuses one whose
/// hash is not the line for its runtime identifier. A file that merely has the right name - a planted copy,
/// a stock build, a native from another release of this package - never gets as far as having its
/// initializers run.
/// </para>
/// </remarks>
internal static class NativeLibraryHashes
{
    /// <summary>The manifest name the table is embedded under.</summary>
    public const string ResourceName = "CodeBrix.Ollama.ModelRunner.Native.SHA256SUMS";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Table =
        new Lazy<IReadOnlyDictionary<string, string>>(LoadEmbedded);

    /// <summary>Every recorded hash, keyed by <c>&lt;rid&gt;/&lt;file name&gt;</c>, lowercase hexadecimal.</summary>
    public static IReadOnlyDictionary<string, string> Recorded => Table.Value;

    /// <summary>The key a runtime identifier and a file name are recorded under.</summary>
    /// <param name="runtimeIdentifier">The runtime identifier, such as <c>linux-x64</c>.</param>
    /// <param name="fileName">The native's file name, such as <c>libcodebrix_llama.so</c>.</param>
    /// <returns>The key.</returns>
    public static string Key(string runtimeIdentifier, string fileName) => runtimeIdentifier + "/" + fileName;

    /// <summary>The recorded hash for one native.</summary>
    /// <param name="runtimeIdentifier">The runtime identifier.</param>
    /// <param name="fileName">The native's file name.</param>
    /// <returns>The lowercase hexadecimal SHA-256, or <see langword="null"/> when nothing is recorded.</returns>
    public static string ExpectedFor(string runtimeIdentifier, string fileName)
    {
        if (runtimeIdentifier == null || fileName == null) return null;
        return Recorded.TryGetValue(Key(runtimeIdentifier, fileName), out string hash) ? hash : null;
    }

    /// <summary>Hashes a stream from its current position to its end.</summary>
    /// <param name="stream">The stream.</param>
    /// <returns>The lowercase hexadecimal SHA-256.</returns>
    public static string Compute(Stream stream) => Convert.ToHexStringLower(SHA256.HashData(stream));

    /// <summary>Parses a table in <c>sha256sum</c> format.</summary>
    /// <param name="reader">The table's text.</param>
    /// <returns>The hashes, keyed by <c>&lt;rid&gt;/&lt;file name&gt;</c>.</returns>
    /// <exception cref="InvalidDataException">
    /// A line is not <c>&lt;64 hex digits&gt;  &lt;rid&gt;/native/&lt;file&gt;</c>, or names one native twice.
    /// </exception>
    /// <remarks>Blank lines and lines starting with <c>#</c> are skipped.</remarks>
    public static IReadOnlyDictionary<string, string> Parse(TextReader reader)
    {
        Dictionary<string, string> table = new Dictionary<string, string>(StringComparer.Ordinal);
        string line;
        int number = 0;

        while ((line = reader.ReadLine()) != null)
        {
            number++;
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#') continue;

            // sha256sum writes "<hash>  <path>", or "<hash> *<path>" in binary mode.
            int space = trimmed.IndexOf(' ');
            string hash = space < 0 ? trimmed : trimmed.Substring(0, space);
            string path = space < 0 ? string.Empty : trimmed.Substring(space + 1).TrimStart(' ', '*');
            string[] parts = path.Split('/');

            if (!IsSha256(hash) || parts.Length != 3 || parts[1] != "native"
                || parts[0].Length == 0 || parts[2].Length == 0)
            {
                throw new InvalidDataException(
                    $"Line {number} of the native hash table is not '<sha256>  <rid>/native/<file>': '{line}'.");
            }

            if (!table.TryAdd(Key(parts[0], parts[2]), hash.ToLowerInvariant()))
            {
                throw new InvalidDataException($"Line {number} of the native hash table names '{path}' a second time.");
            }
        }

        return table;
    }

    private static bool IsSha256(string text)
    {
        if (text.Length != 64) return false;
        foreach (char c in text)
        {
            if (!char.IsAsciiHexDigit(c)) return false;
        }

        return true;
    }

    private static IReadOnlyDictionary<string, string> LoadEmbedded()
    {
        using Stream stream = typeof(NativeLibraryHashes).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new NativeLibraryException(
                $"The native hash table '{ResourceName}' is missing from the CodeBrix.Ollama.ModelRunner "
                + "assembly, so no native library can be verified and none will be loaded.");
        using StreamReader reader = new StreamReader(stream);
        return Parse(reader);
    }
}
