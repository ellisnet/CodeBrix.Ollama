using System;
using System.IO;
using System.Text;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// A copy of one checked-in checkpoint fixture that a test may change - to break a file, to add one, or to say
/// something the fixture does not - removed when the test is done with it.
/// </summary>
/// <remarks>
/// The copy is made inside the test assembly's own output folder rather than the system temporary directory, so
/// that the offline suite writes nothing outside the repository.
/// </remarks>
internal sealed class TempCheckpointDirectory : IDisposable
{
    private static readonly UTF8Encoding Utf8NoPreamble = new UTF8Encoding(false);

    internal TempCheckpointDirectory(string variant)
    {
        DirectoryPath = Path.Combine(AppContext.BaseDirectory, "Convert", "scratch",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        foreach (string file in Directory.GetFiles(ConvertFixtureFiles.CheckpointPath(variant)))
        {
            File.Copy(file, Path.Combine(DirectoryPath, Path.GetFileName(file)));
        }
    }

    /// <summary>The absolute path of the copy.</summary>
    internal string DirectoryPath { get; }

    /// <summary>Replaces one piece of text in the copy's <c>config.json</c>.</summary>
    /// <param name="oldText">The text to find; it must be there.</param>
    /// <param name="newText">What to put in its place.</param>
    internal void RewriteConfig(string oldText, string newText)
    {
        string path = Path.Combine(DirectoryPath, "config.json");
        string content = File.ReadAllText(path, Encoding.UTF8);
        if (!content.Contains(oldText, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The fixture's config.json does not hold \"" + oldText + "\".");
        }

        File.WriteAllText(path, content.Replace(oldText, newText, StringComparison.Ordinal), Utf8NoPreamble);
    }

    /// <summary>Writes a file into the copy.</summary>
    /// <param name="name">The file name.</param>
    /// <param name="content">The bytes.</param>
    internal void WriteFile(string name, byte[] content)
    {
        File.WriteAllBytes(Path.Combine(DirectoryPath, name), content);
    }

    /// <summary>Writes a text file into the copy.</summary>
    /// <param name="name">The file name.</param>
    /// <param name="content">The text.</param>
    internal void WriteText(string name, string content)
    {
        File.WriteAllText(Path.Combine(DirectoryPath, name), content, Utf8NoPreamble);
    }

    /// <summary>Removes a file from the copy.</summary>
    /// <param name="name">The file name.</param>
    internal void DeleteFile(string name)
    {
        File.Delete(Path.Combine(DirectoryPath, name));
    }

    /// <summary>Removes the copy. A failure here never fails a test.</summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
