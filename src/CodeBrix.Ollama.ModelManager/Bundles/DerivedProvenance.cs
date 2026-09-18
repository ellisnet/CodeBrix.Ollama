using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// What a bundle this library produced itself records about how it was produced: which model it came
/// from, which tool made it, that tool's version and the options it was given. It is written into the
/// config layer under the <see cref="ModelConfigKeys"/> names and read back by
/// <see cref="IModelStore.ShowAsync"/>.
/// </summary>
/// <remarks>
/// It is internal on purpose. Provenance is a statement this library makes about work it did, so the
/// only way to write one is to have this library do that work; a consumer importing a folder of its own
/// states a licence and nothing else.
/// </remarks>
internal sealed class DerivedProvenance
{
    /// <summary>
    /// Initializes a provenance record.
    /// </summary>
    /// <param name="sourceName">The model this one was derived from, spelled as it is stored.</param>
    /// <param name="tool">The tool that produced the files.</param>
    /// <param name="toolVersion">That tool's version, as the tool itself reports it.</param>
    /// <param name="settings">The options the tool was given; <see langword="null"/> for none.</param>
    /// <param name="format">
    /// The model format to record when the file names decide nothing, usually
    /// <see cref="BundleFormatDetector.Derived"/>.
    /// </param>
    /// <param name="license">What is known about the licence, carried over from the source.</param>
    internal DerivedProvenance(
        string sourceName,
        string tool,
        string toolVersion,
        IReadOnlyDictionary<string, string> settings,
        string format,
        LicenseRecord license)
    {
        SourceName = sourceName;
        Tool = tool;
        ToolVersion = toolVersion;
        Settings = settings ?? new Dictionary<string, string>(StringComparer.Ordinal);
        Format = format ?? BundleFormatDetector.Derived;
        License = license ?? LicenseRecord.None;
    }

    /// <summary>The model this one was derived from, spelled as it is stored.</summary>
    internal string SourceName { get; }

    /// <summary>The tool that produced the files.</summary>
    internal string Tool { get; }

    /// <summary>That tool's version.</summary>
    internal string ToolVersion { get; }

    /// <summary>The options the tool was given. Never <see langword="null"/>.</summary>
    internal IReadOnlyDictionary<string, string> Settings { get; }

    /// <summary>The model format to record when the file names decide nothing.</summary>
    internal string Format { get; }

    /// <summary>
    /// What is known about the licence. A derived bundle carries the source's licence over unchanged,
    /// because deriving a file from a model does not change whose terms it is under. It is REPORTED,
    /// never enforced.
    /// </summary>
    internal LicenseRecord License { get; }
}
