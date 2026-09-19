using System;
using System.IO;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// An empty folder a test may write into, removed when the test is done with it.
/// </summary>
/// <remarks>
/// It sits inside the test assembly's own output folder rather than the system temporary directory, so that
/// the offline suite writes nothing outside the repository. It is a copy of the helper the store's suite
/// carries rather than a shared one, because no test project here references another.
/// </remarks>
internal sealed class TempScratchDirectory : IDisposable
{
    internal TempScratchDirectory()
    {
        DirectoryPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "scratch",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
    }

    /// <summary>The absolute path of the folder.</summary>
    internal string DirectoryPath { get; }

    /// <summary>The path of a file inside the folder.</summary>
    /// <param name="name">The file name.</param>
    /// <returns>The path.</returns>
    internal string Combine(string name)
    {
        return Path.Combine(DirectoryPath, name);
    }

    /// <summary>Removes the folder and everything in it. A failure here never fails a test.</summary>
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
