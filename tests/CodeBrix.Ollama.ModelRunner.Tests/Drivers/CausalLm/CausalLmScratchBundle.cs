using System;
using System.IO;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// A copy of the tiny bundle in a folder of its own, for the tests that break one on purpose or change what
/// it says about itself.
/// </summary>
/// <remarks>
/// It is made INSIDE the test assembly's own output folder, which is the only place this suite writes: a run
/// of it reaches nothing outside the repository. Disposing it takes the copy away again.
/// </remarks>
public sealed class CausalLmScratchBundle : IDisposable
{
    /// <summary>Copies the tiny bundle into a folder of its own.</summary>
    public CausalLmScratchBundle()
    {
        DirectoryPath = Path.Combine(
            AppContext.BaseDirectory, "scratch-causallm", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);

        foreach (string path in Directory.EnumerateFiles(CausalLmFixtures.TinyBundleDirectory))
        {
            File.Copy(path, Path.Combine(DirectoryPath, Path.GetFileName(path)));
        }
    }

    /// <summary>Where the copy is.</summary>
    public string DirectoryPath { get; }

    /// <summary>The copy's generation configuration, as it stands.</summary>
    /// <returns>The JSON.</returns>
    public string ReadConfiguration() =>
        File.ReadAllText(Path.Combine(DirectoryPath, "genai_config.json"));

    /// <summary>Replaces the copy's generation configuration.</summary>
    /// <param name="json">What to write in its place.</param>
    public void WriteConfiguration(string json) =>
        File.WriteAllText(Path.Combine(DirectoryPath, "genai_config.json"), json);

    /// <summary>Changes one number in the copy's generation configuration.</summary>
    /// <param name="name">The property's name, as it is written in the file.</param>
    /// <param name="was">The value it has.</param>
    /// <param name="becomes">The value it is to have.</param>
    public void Replace(string name, string was, string becomes)
    {
        string text = ReadConfiguration();
        string from = "\"" + name + "\": " + was;
        string to = "\"" + name + "\": " + becomes;
        if (!text.Contains(from, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The tiny bundle's configuration does not hold '" + from + "'.");
        }

        WriteConfiguration(text.Replace(from, to, StringComparison.Ordinal));
    }

    /// <summary>Takes one of the copy's files away.</summary>
    /// <param name="name">The file's name.</param>
    public void Remove(string name)
    {
        string path = Path.Combine(DirectoryPath, name);
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>Adds a file to the copy.</summary>
    /// <param name="name">The file's name.</param>
    /// <param name="content">What is in it.</param>
    public void Add(string name, string content) =>
        File.WriteAllText(Path.Combine(DirectoryPath, name), content);

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
