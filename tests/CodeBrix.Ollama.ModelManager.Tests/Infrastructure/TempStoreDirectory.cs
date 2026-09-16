using System;
using System.IO;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// A unique, empty store directory under the system temporary directory, removed when the test is
/// done with it. Every store test works inside one of these so that nothing is ever written outside
/// the test's own scratch space.
/// </summary>
public sealed class TempStoreDirectory : IDisposable
{
    /// <summary>
    /// Creates the directory.
    /// </summary>
    public TempStoreDirectory()
    {
        DirectoryPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "codebrix-ollama-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        Paths = new ModelStorePaths(DirectoryPath);
    }

    /// <summary>
    /// The absolute path of the store directory.
    /// </summary>
    public string DirectoryPath { get; }

    /// <summary>
    /// The store paths rooted at <see cref="DirectoryPath"/>.
    /// </summary>
    internal ModelStorePaths Paths { get; }

    /// <summary>
    /// Removes the directory and everything in it. Failures are ignored: a leftover temporary
    /// directory must never fail a test.
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
