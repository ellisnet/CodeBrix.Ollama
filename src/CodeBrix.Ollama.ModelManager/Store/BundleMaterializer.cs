using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Writes the files of a bundle out as the directory tree its publisher wrote, from the blobs the store
/// keeps them in. The store is content addressed and says nothing about where a file belongs; the
/// manifest's layer names do, and this is where they are turned back into paths.
/// </summary>
/// <remarks>
/// Every path is checked against the target directory before anything is written. A layer name comes
/// from a manifest on disk, which a bundle pull wrote but which nothing stops a third party from
/// editing, so a name that is rooted, that walks up with <c>..</c> or that otherwise lands outside the
/// target directory fails the call rather than writing where it was told to.
/// </remarks>
internal static class BundleMaterializer
{
    /// <summary>The buffer a copy is streamed through.</summary>
    private const int BufferSize = 128 * 1024;

    /// <summary>
    /// Writes every file of a bundle under a target directory.
    /// </summary>
    /// <param name="files">The files, in manifest order.</param>
    /// <param name="targetDirectory">The directory the tree is written under. It is created if missing.</param>
    /// <param name="options">How each file is written and what happens to a file already there.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    /// <returns>The absolute paths that were written, in the order the files were given.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ModelManagerException">
    /// A layer names a blob that is not in the store or a path outside the target directory; a file is
    /// already at a target path and <see cref="MaterializeOptions.Overwrite"/> is not set; or a
    /// symbolic link was asked for and could not be made.
    /// </exception>
    public static async Task<IReadOnlyList<string>> WriteAsync(
        IReadOnlyList<ResolvedFile> files,
        string targetDirectory,
        MaterializeOptions options,
        CancellationToken cancellationToken)
    {
        if (files == null)
        {
            throw new ArgumentNullException(nameof(files));
        }
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            throw new ArgumentException("A target directory is required.", nameof(targetDirectory));
        }
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetDirectory));
        Directory.CreateDirectory(root);

        var written = new List<string>(files.Count);
        foreach (ResolvedFile file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string targetPath = ResolveTargetPath(root, file.Name);
            if (!File.Exists(file.BlobPath))
            {
                throw new ModelManagerException(
                    "blob " + file.Digest + " of '" + file.Name + "' does not exist");
            }

            string directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            PrepareTarget(targetPath, options.Overwrite);
            await WriteOneAsync(file, targetPath, options.Link, cancellationToken).ConfigureAwait(false);
            written.Add(targetPath);
        }

        return written;
    }

    /// <summary>
    /// Turns one layer name into the absolute path it is written to, and refuses a name that would land
    /// anywhere but under the target directory.
    /// </summary>
    /// <param name="root">The absolute target directory, with no trailing separator.</param>
    /// <param name="name">The layer name, which is the publisher's relative path.</param>
    /// <returns>The absolute path.</returns>
    /// <exception cref="ModelManagerException">The name is missing or would escape the target directory.</exception>
    private static string ResolveTargetPath(string root, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ModelManagerException(
                "a bundle file layer carries no name, so there is nowhere to write it");
        }

        string relative = name.Replace('\\', '/').Trim();
        foreach (string segment in relative.Split('/'))
        {
            if (segment == "..")
            {
                throw new ModelManagerException(
                    "the bundle file path '" + name + "' walks outside the directory it would be written to");
            }
        }

        if (relative.StartsWith("/", StringComparison.Ordinal)
            || (relative.Length > 1 && relative[1] == ':'))
        {
            throw new ModelManagerException(
                "the bundle file path '" + name + "' is absolute; a bundle file path is relative to the bundle root");
        }

        string combined = Path.GetFullPath(
            Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));

        if (!combined.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ModelManagerException(
                "the bundle file path '" + name + "' would be written outside " + root);
        }

        return combined;
    }

    /// <summary>
    /// Clears whatever is at a target path, or refuses to.
    /// </summary>
    /// <param name="targetPath">The path about to be written.</param>
    /// <param name="overwrite">Whether a file already there may be replaced.</param>
    /// <exception cref="ModelManagerException">Something is there and may not be replaced.</exception>
    private static void PrepareTarget(string targetPath, bool overwrite)
    {
        if (Directory.Exists(targetPath))
        {
            throw new ModelManagerException(
                "a directory is already at " + targetPath + ", so the bundle file cannot be written there");
        }

        if (!File.Exists(targetPath))
        {
            return;
        }

        if (!overwrite)
        {
            throw new ModelManagerException(
                "a file is already at " + targetPath
                    + "; set MaterializeOptions.Overwrite to replace what is already there");
        }

        File.Delete(targetPath);
    }

    /// <summary>
    /// Puts one file in place.
    /// </summary>
    /// <param name="file">The file to write.</param>
    /// <param name="targetPath">Where it goes. Nothing is there.</param>
    /// <param name="link">How it is put in place.</param>
    /// <param name="cancellationToken">A token that cancels a copy.</param>
    /// <returns>A task that completes when the file is in place.</returns>
    /// <exception cref="ModelManagerException">A symbolic link was asked for and could not be made.</exception>
    private static async Task WriteOneAsync(
        ResolvedFile file,
        string targetPath,
        MaterializeLink link,
        CancellationToken cancellationToken)
    {
        if (link == MaterializeLink.Symlink)
        {
            try
            {
                File.CreateSymbolicLink(targetPath, file.BlobPath);
            }
            catch (Exception exception) when (exception is IOException
                || exception is UnauthorizedAccessException
                || exception is PlatformNotSupportedException)
            {
                throw new ModelManagerException(
                    "a symbolic link for '" + file.Name + "' could not be made at " + targetPath
                        + ". Symbolic links are never quietly turned into copies: ask for"
                        + " MaterializeLink.Hardlink or MaterializeLink.Copy instead.",
                    exception);
            }
            return;
        }

        if (link == MaterializeLink.Hardlink && HardLink.TryCreate(file.BlobPath, targetPath))
        {
            return;
        }

        await CopyAsync(file.BlobPath, targetPath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Copies a blob to a target path.
    /// </summary>
    /// <param name="blobPath">The blob to read.</param>
    /// <param name="targetPath">The file to write.</param>
    /// <param name="cancellationToken">A token that cancels the copy.</param>
    /// <returns>A task that completes when every byte has been written.</returns>
    private static async Task CopyAsync(string blobPath, string targetPath, CancellationToken cancellationToken)
    {
        await using FileStream source = new FileStream(
            blobPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await using FileStream destination = new FileStream(
            targetPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous);

        await source.CopyToAsync(destination, BufferSize, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
