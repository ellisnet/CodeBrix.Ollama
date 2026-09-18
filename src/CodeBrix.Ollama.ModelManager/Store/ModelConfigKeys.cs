namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The names of the config-layer properties this library writes beside the ones Ollama's own config
/// models. They are read back through <see cref="ModelConfig.AdditionalProperties"/>, which is a
/// dictionary keyed by exactly these strings, so a consumer that wants a field this library does not
/// surface as a property of its own has a name to ask for rather than a spelling to guess.
/// </summary>
/// <remarks>
/// <para>
/// Every one of them carries a JSON string or a JSON null, except <see cref="Settings"/>, which carries
/// a JSON object whose values are strings. A property written as null means "this was written by this
/// library and there was nothing to say"; a property that is absent altogether means the config was
/// written by something that did not know about it.
/// </para>
/// <para>
/// The first six are written by every bundle pull and every folder import. The last five are written
/// only by a DERIVED bundle - one this library produced itself from another model in the store - and
/// are what its provenance is made of.
/// </para>
/// </remarks>
public static class ModelConfigKeys
{
    /// <summary>Where a bundle's files came from: "hf.co", "url", "local" or "derived".</summary>
    public const string Source = "source";

    /// <summary>The repository or folder a bundle came from, or null.</summary>
    public const string Repository = "repository";

    /// <summary>The commit a Hugging Face listing resolved to, or null.</summary>
    public const string Revision = "revision";

    /// <summary>The licence identifier the source states, or null. It is REPORTED, never enforced.</summary>
    public const string LicenseId = "licenseId";

    /// <summary>Where the licence statement was read from, or null.</summary>
    public const string LicenseSource = "licenseSource";

    /// <summary>When the bundle was pulled or imported, as a round-trip UTC timestamp.</summary>
    public const string PulledAt = "pulledAt";

    /// <summary>
    /// The model this one was derived from, spelled exactly as that model is stored, or absent when the
    /// bundle was not derived from anything.
    /// </summary>
    public const string DerivedFrom = "derivedFrom";

    /// <summary>
    /// The tool that produced a derived bundle: "onnxruntime-genai", "optimum" or "publisher".
    /// </summary>
    public const string Tool = "tool";

    /// <summary>The version of that tool, as the tool itself reports it.</summary>
    public const string ToolVersion = "toolVersion";

    /// <summary>
    /// The options the tool was run with, as a JSON object whose values are strings.
    /// </summary>
    public const string Settings = "settings";

    /// <summary>When the derived bundle was produced, as a round-trip UTC timestamp.</summary>
    public const string DerivedAt = "derivedAt";
}
