using System;
using System.IO;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// A copy of the tiny bundle in a folder of its own, for the tests that break one on purpose.
/// </summary>
/// <remarks>
/// It is made INSIDE the test assembly's own output folder, which is the only place this suite writes: a run
/// of it reaches nothing outside the repository. Disposing it takes the copy away again.
/// </remarks>
public sealed class SkyTntScratchBundle : IDisposable
{
    /// <summary>Copies the tiny bundle into a folder of its own.</summary>
    public SkyTntScratchBundle()
    {
        DirectoryPath = Path.Combine(
            AppContext.BaseDirectory, "scratch-skytnt", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(DirectoryPath, "onnx"));

        File.Copy(
            Path.Combine(SkyTntFixtures.TinyModelDirectory, "config.json"),
            Path.Combine(DirectoryPath, "config.json"));
        foreach (string graph in new[] { "model_base.onnx", "model_token.onnx" })
        {
            File.Copy(
                Path.Combine(SkyTntFixtures.TinyModelDirectory, "onnx", graph),
                Path.Combine(DirectoryPath, "onnx", graph));
        }
    }

    /// <summary>Where the copy is.</summary>
    public string DirectoryPath { get; }

    /// <summary>Replaces the copy's description of itself.</summary>
    /// <param name="json">What to write in its place.</param>
    public void WriteConfiguration(string json) =>
        File.WriteAllText(Path.Combine(DirectoryPath, "config.json"), json);

    /// <summary>The tokenizer block of the real description, for building one that differs elsewhere.</summary>
    /// <returns>The block, as JSON.</returns>
    public string TokenizerBlock()
    {
        string text = File.ReadAllText(Path.Combine(SkyTntFixtures.TinyModelDirectory, "config.json"));
        int at = text.IndexOf("\"tokenizer\"", StringComparison.Ordinal);
        int open = text.IndexOf('{', at);
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            if (text[i] == '}') depth--;
            if (depth == 0) return text.Substring(open, i - open + 1);
        }

        throw new InvalidOperationException("The tiny bundle's tokenizer block could not be found.");
    }

    /// <summary>Takes the copy away again.</summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, recursive: true);
        }
        catch (IOException)
        {
            // A folder another test still holds open is left where it is; it is inside the output folder.
        }
    }
}
