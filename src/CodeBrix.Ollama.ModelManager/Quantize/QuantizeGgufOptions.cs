namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// How a stored GGUF model is quantized: who does the quantizing, what the type is called, what to call the
/// result, whether an existing model of that name may be replaced, and what to record as having done the work.
/// </summary>
/// <remarks>
/// Only <see cref="Quantizer"/> and <see cref="Type"/> have to be set. Everything else has a default, and the
/// two that name the tool are only written into the result's provenance - the store believes what it is told
/// about them, because the store is not the one doing the work.
/// </remarks>
public sealed class QuantizeGgufOptions
{
    /// <summary>
    /// The quantizer that reads the stored file and writes the quantized one. It is required: this library
    /// has no quantizer of its own.
    /// </summary>
    public GgufQuantizer Quantizer { get; set; }

    /// <summary>
    /// What the quantization is called, in the spelling that goes into the stored model's name and its
    /// provenance - for example <c>q4_k_m</c>, which stores the result as <c>&lt;source&gt;:gguf-q4_k_m</c>.
    /// It is required.
    /// </summary>
    /// <remarks>
    /// The store never interprets it. It is a tag, so it is lower-cased and must be made of letters, digits,
    /// dots, dashes and underscores - the characters a model name allows - and anything else fails with an
    /// <see cref="System.ArgumentException"/> naming what was given.
    /// </remarks>
    public string Type { get; set; }

    /// <summary>
    /// The name to store the result under. When it is <see langword="null"/> the source name is used with a
    /// tag that names the quantization.
    /// </summary>
    public string OutputName { get; set; }

    /// <summary>Whether an existing model of the output name may be replaced.</summary>
    public bool Overwrite { get; set; }

    /// <summary>
    /// The name of the tool that did the quantizing, recorded as the result's provenance. When it is
    /// <see langword="null"/> nothing is claimed about it.
    /// </summary>
    public string Tool { get; set; }

    /// <summary>
    /// The version of the tool that did the quantizing, recorded as the result's provenance. When it is
    /// <see langword="null"/> nothing is claimed about it.
    /// </summary>
    public string ToolVersion { get; set; }
}
