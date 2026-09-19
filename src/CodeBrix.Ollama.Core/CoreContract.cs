using System;
using System.Globalization;

namespace CodeBrix.Ollama.Core;

/// <summary>
/// The compatibility contract between this assembly and the libraries that carry it.
/// </summary>
/// <remarks>
/// <para>
/// THIS TYPE IS PERMANENT. It is never renamed, never removed and never made non-static, and
/// <see cref="Revision"/>, <see cref="LoadedRevision"/> and <see cref="Require"/> keep their names and
/// their shapes for as long as this project exists. Everything else in this assembly may be reshaped
/// freely; this one type is what a library built against an older copy still knows how to call, so
/// changing it would take away the very message it exists to produce.
/// </para>
/// <para>
/// WHY IT EXISTS. CodeBrix.Ollama.ModelManager and CodeBrix.Ollama.ModelRunner each pack a copy of this
/// assembly inside their own package. They are published together and are meant to be installed together,
/// but nothing stops an application from installing two different versions of them. When it does, the SDK
/// silently hands the application ONE of the two copies of this assembly - the higher-versioned one - and
/// the other library then finds a member it was compiled against missing, which surfaces as a
/// <see cref="MissingMethodException"/> or a <see cref="MissingMemberException"/> from somewhere deep
/// inside a call that looks unrelated. The guard turns that into a sentence that says what to do.
/// </para>
/// <para>
/// HOW IT WORKS. <see cref="Revision"/> is a <see langword="const"/>, so the C# compiler bakes its value
/// into every library that reads it, at that library's OWN compile time. <see cref="LoadedRevision"/> is
/// a field, so it is read from whichever copy of this assembly the application actually loaded. A library
/// passes the number it was built with to <see cref="Require"/> and the two are compared. It is a REVISION
/// and never a version: this family stamps every assembly's version to the minute it was built, so inside
/// a development tree this assembly's version differs from both libraries' after any incremental build and
/// comparing versions would fail constantly and mean nothing.
/// </para>
/// <para>
/// WHEN TO BUMP IT. Raise <see cref="Revision"/> by one whenever anything in this assembly that a library
/// can reach changes in a way an older library would not survive: a type or member renamed or removed, a
/// signature changed, a constant's value changed, or a behaviour a caller depends on redefined. Adding a
/// brand-new type or member that nothing older calls does not need a bump, but bumping it costs nothing.
/// The surface-hash test in CodeBrix.Ollama.Core.Tests fails on any change at all, so a forgotten bump
/// cannot ship.
/// </para>
/// </remarks>
internal static class CoreContract
{
    /// <summary>
    /// The revision of this assembly's internal surface. A library reads this constant at its own compile
    /// time and hands the value back to <see cref="Require"/> at run time.
    /// </summary>
    internal const int Revision = 1;

    /// <summary>
    /// The revision of the copy of this assembly that was actually loaded. Read from a field rather than
    /// from the constant, so it is the LOADED assembly's number and not the calling library's.
    /// </summary>
    internal static readonly int LoadedRevision = Revision;

    /// <summary>
    /// Checks that the library calling in was built against the copy of this assembly that was loaded.
    /// </summary>
    /// <param name="builtAgainst">
    /// The caller's own <see cref="Revision"/>, which its compiler baked in when it was built.
    /// </param>
    /// <param name="library">The calling library's assembly name, which the message names.</param>
    /// <exception cref="InvalidOperationException">
    /// The caller was built against a different revision from the one that was loaded, which means the
    /// application has installed mismatched versions of the CodeBrix.Ollama packages.
    /// </exception>
    internal static void Require(int builtAgainst, string library)
    {
        if (builtAgainst != LoadedRevision)
        {
            ThrowMismatch(builtAgainst, library);
        }
    }

    private static void ThrowMismatch(int builtAgainst, string library)
    {
        throw new InvalidOperationException(
            (library == null ? "A CodeBrix.Ollama library" : library) +
            " was built against CodeBrix.Ollama.Core contract revision " +
            builtAgainst.ToString(CultureInfo.InvariantCulture) +
            ", but contract revision " +
            LoadedRevision.ToString(CultureInfo.InvariantCulture) +
            " was loaded. The CodeBrix.Ollama packages are published together and each one carries its own " +
            "copy of this assembly, so a mixture of versions leaves one of them running against the other's " +
            "copy. Install the SAME version of every CodeBrix.Ollama package.");
    }
}
