using System;
using System.Collections.Generic;
using System.IO;

namespace ModelQueryTool.ModelAccess;

/// <summary>
/// The file-system work the stager does around the store library: measuring what lies under a folder,
/// and taking empty folders away again so that removing a model really does leave nothing behind.
/// </summary>
/// <remarks>
/// Every method here is deliberately timid. A folder is removed ONLY when it holds nothing at all, never
/// recursively, and never when it is a symbolic link - so a foreign file someone put in the folder keeps
/// the folder, and a link to somewhere else is left exactly where it was.
/// </remarks>
internal static class StoreFolders
{
    /// <summary>The folder inside the store that holds the manifests.</summary>
    public const string ManifestsFolderName = "manifests";

    /// <summary>The folder inside the store that holds the content-addressed files.</summary>
    public const string BlobsFolderName = "blobs";

    /// <summary>
    /// Adds up what lies under a folder without following a symbolic link out of it.
    /// </summary>
    /// <param name="directory">The folder to measure. It need not exist.</param>
    /// <returns>How many bytes were found and whether there was a file at all.</returns>
    public static StoreContents Measure(string directory)
    {
        var contents = new StoreContents();
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory) || IsSymbolicLink(directory))
        {
            return contents;
        }

        var pending = new Stack<string>();
        pending.Push(directory);

        while (pending.Count > 0)
        {
            string current = pending.Pop();
            IEnumerable<string> files;
            IEnumerable<string> directories;
            try
            {
                files = Directory.EnumerateFiles(current);
                directories = Directory.EnumerateDirectories(current);
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string file in files)
            {
                contents.Add(GetLengthOrZero(file));
            }

            foreach (string child in directories)
            {
                if (!IsSymbolicLink(child))
                {
                    pending.Push(child);
                }
            }
        }

        return contents;
    }

    /// <summary>
    /// Takes away, one at a time and only while each is empty, every folder under <c>manifests/</c>,
    /// then <c>manifests/</c>, then <c>blobs/</c>, then the store folder, then the application's own
    /// folder. The first one that still holds something stops the sweep where it stands.
    /// </summary>
    /// <param name="storeDirectory">The store folder.</param>
    /// <param name="rootDirectory">The application's own folder, which holds the store folder.</param>
    public static void SweepEmptyFolders(string storeDirectory, string rootDirectory)
    {
        string manifests = Path.Combine(storeDirectory, ManifestsFolderName);
        RemoveEmptyFoldersUnder(manifests);
        TryRemoveEmptyFolder(manifests);
        TryRemoveEmptyFolder(Path.Combine(storeDirectory, BlobsFolderName));
        TryRemoveEmptyFolder(storeDirectory);
        TryRemoveEmptyFolder(rootDirectory);
    }

    /// <summary>
    /// Deletes a file, saying nothing when it is not there or the disk will not part with it.
    /// </summary>
    /// <param name="path">The file to delete.</param>
    /// <returns><see langword="true"/> when no file is at that path afterwards.</returns>
    public static bool TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            return !File.Exists(path);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether a path is a symbolic link, which is never followed and never removed.
    /// </summary>
    /// <param name="path">The path to look at.</param>
    /// <returns><see langword="true"/> when the path is a link to somewhere else.</returns>
    public static bool IsSymbolicLink(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            if (info.Exists)
            {
                return info.LinkTarget != null;
            }

            var file = new FileInfo(path);
            return file.Exists && file.LinkTarget != null;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// Removes every empty folder below a folder, deepest first, and leaves the folder itself alone.
    /// </summary>
    /// <param name="directory">The folder to sweep inside.</param>
    private static void RemoveEmptyFoldersUnder(string directory)
    {
        if (!Directory.Exists(directory) || IsSymbolicLink(directory))
        {
            return;
        }

        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(directory);
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        foreach (string child in children)
        {
            if (IsSymbolicLink(child))
            {
                continue;
            }

            RemoveEmptyFoldersUnder(child);
            TryRemoveEmptyFolder(child);
        }
    }

    /// <summary>
    /// Removes a folder when it holds nothing at all, and otherwise leaves it exactly as it is.
    /// </summary>
    /// <param name="directory">The folder to remove.</param>
    private static void TryRemoveEmptyFolder(string directory)
    {
        try
        {
            if (string.IsNullOrEmpty(directory)
                || !Directory.Exists(directory)
                || IsSymbolicLink(directory))
            {
                return;
            }

            foreach (string unused in Directory.EnumerateFileSystemEntries(directory))
            {
                return;
            }

            Directory.Delete(directory, false);
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// The length of a file, or zero when it has gone since the walk found it.
    /// </summary>
    /// <param name="path">The file to measure.</param>
    /// <returns>The length in bytes.</returns>
    private static long GetLengthOrZero(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : 0L;
        }
        catch (IOException)
        {
            return 0L;
        }
        catch (UnauthorizedAccessException)
        {
            return 0L;
        }
    }
}
