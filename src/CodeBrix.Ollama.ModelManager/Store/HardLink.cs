using System;
using System.IO;
using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Makes a hard link, which .NET itself has no API for. A hard link is a second directory entry for
/// bytes already on disk, so it costs nothing to make and nothing to keep, which is what lets a bundle
/// be laid out as a file tree without a second copy of every weights file.
/// </summary>
/// <remarks>
/// <para>
/// The platform calls are the smallest that can be written: two entry points, no structures, no
/// handles, no callbacks, declared in the source-generated form the family uses everywhere.
/// </para>
/// <para>
/// Nothing here throws. A platform with no hard links, a file system that will not make one, a link
/// across a volume and a link the caller is not allowed to make all answer the same way - the link was
/// not made - because every caller's next step is the same either way: copy the bytes instead.
/// </para>
/// </remarks>
internal static partial class HardLink
{
    /// <summary>
    /// Makes a hard link at <paramref name="linkPath"/> to the file at <paramref name="existingPath"/>.
    /// </summary>
    /// <param name="existingPath">The file that already exists.</param>
    /// <param name="linkPath">The path of the new directory entry. Nothing may be there yet.</param>
    /// <returns>
    /// <see langword="true"/> when the link was made; <see langword="false"/> when it was not, whatever
    /// the reason.
    /// </returns>
    public static bool TryCreate(string existingPath, string linkPath)
    {
        if (string.IsNullOrEmpty(existingPath) || string.IsNullOrEmpty(linkPath))
        {
            return false;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                return CreateHardLinkW(linkPath, existingPath, IntPtr.Zero);
            }

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
            {
                return LinkUnix(existingPath, linkPath) == 0;
            }
        }
        catch (DllNotFoundException)
        {
            // The platform library is not where the runtime looked, so there is no linking here.
        }
        catch (EntryPointNotFoundException)
        {
            // The library is there but does not offer the call, which comes to the same thing.
        }
        catch (IOException)
        {
            // Whatever the file system objected to, a copy is the answer.
        }
        catch (UnauthorizedAccessException)
        {
            // As above: linking is not allowed here, so the caller copies.
        }

        return false;
    }

    /// <summary>
    /// The POSIX <c>link</c> call.
    /// </summary>
    /// <param name="oldPath">The file that already exists.</param>
    /// <param name="newPath">The path of the new directory entry.</param>
    /// <returns>Zero when the link was made, -1 when it was not.</returns>
    [LibraryImport("libc", EntryPoint = "link", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int LinkUnix(string oldPath, string newPath);

    /// <summary>
    /// The Windows <c>CreateHardLinkW</c> call.
    /// </summary>
    /// <param name="linkPath">The path of the new directory entry.</param>
    /// <param name="existingPath">The file that already exists.</param>
    /// <param name="securityAttributes">Always <see cref="IntPtr.Zero"/>; the parameter is reserved.</param>
    /// <returns><see langword="true"/> when the link was made.</returns>
    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLinkW(string linkPath, string existingPath, IntPtr securityAttributes);
}
