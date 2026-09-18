using System;
using System.IO;

namespace CodeBrix.Ollama.ModelManager.Python.Tests;

/// <summary>
/// A unique, empty directory under the system temporary directory, removed when the test is done with
/// it. The export tests lay a derived bundle out in one to check that materializing works; a store's own
/// directory is not temporary here, but everything written out of it is.
/// </summary>
public sealed class TempExportDirectory : IDisposable
{
    /// <summary>
    /// Creates the directory.
    /// </summary>
    public TempExportDirectory()
    {
        DirectoryPath = Path.Combine(
            Path.GetTempPath(), "codebrix-ollama-export-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
    }

    /// <summary>The absolute path of the directory.</summary>
    public string DirectoryPath { get; }

    /// <summary>
    /// Removes the directory and everything in it. Failures are ignored: a leftover temporary directory
    /// must never fail a test.
    /// </summary>
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
