using System;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Thrown when a feature that needs CPython is asked for and there is no usable CPython shared library:
/// none was found, the one that was found will not load, or its version is outside the supported range.
/// The message always ends with the feature that needed it.
/// </summary>
public class PythonNotAvailableException : ModelManagerException
{
    /// <summary>
    /// The sentence used when nothing named a library at all.
    /// </summary>
    private const string NotFoundReason =
        "No CPython shared library was found. Set PythonOptions.LibraryPath, Runtime.PythonDLL or the"
        + " PYTHONNET_PYDLL environment variable to libpython3.XX.so / python3XX.dll /"
        + " libpython3.XX.dylib;";

    /// <summary>
    /// Initializes a new instance of the <see cref="PythonNotAvailableException"/> class for the case
    /// where no CPython shared library was found anywhere.
    /// </summary>
    /// <param name="feature">The feature that needed CPython, named as a phrase - "exporting to ONNX".</param>
    /// <param name="minimumVersion">The lowest CPython version the embedding layer supports.</param>
    /// <param name="maximumVersion">The highest CPython version the embedding layer supports.</param>
    public PythonNotAvailableException(string feature, Version minimumVersion, Version maximumVersion)
        : this(feature, null, minimumVersion, maximumVersion)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PythonNotAvailableException"/> class.
    /// </summary>
    /// <param name="feature">The feature that needed CPython, named as a phrase - "exporting to ONNX".</param>
    /// <param name="reason">
    /// A sentence saying what is wrong with the library that was found - that it does not exist, that it
    /// will not load, or that its version is out of range. <see langword="null"/> for the case where
    /// nothing named a library at all.
    /// </param>
    /// <param name="minimumVersion">The lowest CPython version the embedding layer supports.</param>
    /// <param name="maximumVersion">The highest CPython version the embedding layer supports.</param>
    public PythonNotAvailableException(string feature, string reason, Version minimumVersion, Version maximumVersion)
        : base(BuildMessage(feature, reason, minimumVersion, maximumVersion))
    {
        Feature = feature;
    }

    /// <summary>
    /// The feature that needed CPython, as the caller named it.
    /// </summary>
    public string Feature { get; }

    /// <summary>
    /// Composes the message: what is wrong, then the supported range, then the feature that needed it.
    /// </summary>
    /// <param name="feature">The feature that needed CPython.</param>
    /// <param name="reason">What is wrong, or <see langword="null"/> for "nothing was found".</param>
    /// <param name="minimumVersion">The lowest supported CPython version.</param>
    /// <param name="maximumVersion">The highest supported CPython version.</param>
    /// <returns>The complete message.</returns>
    private static string BuildMessage(string feature, string reason, Version minimumVersion, Version maximumVersion)
    {
        string what = string.IsNullOrWhiteSpace(reason) ? NotFoundReason : reason.Trim();
        string range = " the supported range is " + Describe(minimumVersion) + " to " + Describe(maximumVersion) + ".";
        if (!what.EndsWith(";", StringComparison.Ordinal))
        {
            range = " The supported range is " + Describe(minimumVersion) + " to " + Describe(maximumVersion) + ".";
        }
        string needed = string.IsNullOrWhiteSpace(feature)
            ? string.Empty
            : " It is needed for " + feature.Trim() + ".";
        return what + range + needed;
    }

    /// <summary>
    /// Renders a version as the major.minor pair the range is really expressed in.
    /// </summary>
    /// <param name="version">The version to render, or <see langword="null"/>.</param>
    /// <returns>"3.10", or "(unknown)" when there is no version.</returns>
    private static string Describe(Version version) => version == null ? "(unknown)" : version.ToString(2);
}
