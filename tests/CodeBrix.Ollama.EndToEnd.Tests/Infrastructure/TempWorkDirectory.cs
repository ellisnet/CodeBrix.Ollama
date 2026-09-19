using System;
using System.IO;

namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>
/// A unique, empty directory under the system temporary directory, removed when the test is done with it. The
/// checkpoint a test hands to another tool is laid out in one of these, and so is anything that tool writes.
/// </summary>
/// <remarks>
/// A checkpoint is hundreds of megabytes, so on a machine whose temporary directory is in memory - which is
/// most Linux desktops - TMPDIR must point at a real file system before these tests are opened.
/// </remarks>
public sealed class TempWorkDirectory : IDisposable
{
    /// <summary>
    /// Creates the directory.
    /// </summary>
    /// <param name="name">
    /// The name of a sub-folder to create inside it and report, or <see langword="null"/> for none. The
    /// inference engine's converter derives a model's general metadata from the name of the folder it is
    /// pointed at, so a test that compares against it names the folder deliberately.
    /// </param>
    public TempWorkDirectory(string name = null)
    {
        RootPath = Path.Combine(
            Path.GetTempPath(), "codebrix-ollama-endtoend", Guid.NewGuid().ToString("N"));
        DirectoryPath = name == null ? RootPath : Path.Combine(RootPath, name);
        Directory.CreateDirectory(DirectoryPath);
    }

    /// <summary>The absolute path of the directory that is removed.</summary>
    public string RootPath { get; }

    /// <summary>The absolute path a test writes into.</summary>
    public string DirectoryPath { get; }

    /// <summary>The path of a file inside the root.</summary>
    /// <param name="fileName">The file name.</param>
    /// <returns>The path.</returns>
    public string Combine(string fileName) => Path.Combine(RootPath, fileName);

    /// <summary>
    /// Removes the directory and everything in it. Failures are ignored: a leftover temporary directory must
    /// never fail a test.
    /// </summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, true);
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
