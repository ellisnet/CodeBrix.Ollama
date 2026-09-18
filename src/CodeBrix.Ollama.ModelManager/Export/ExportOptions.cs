namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// What an export to ONNX is asked to do: which route to take, at what precision, what to call the
/// bundle it writes, and whether it may replace one that is already there.
/// </summary>
/// <remarks>
/// Every property has a value that is safe when nothing is said, so an instance built with an object
/// initializer that sets only what it cares about is always usable.
/// </remarks>
public sealed class ExportOptions
{
    private string _precision = DefaultPrecision;

    /// <summary>The precision an export runs at when the caller names none.</summary>
    public const string DefaultPrecision = "fp32";

    /// <summary>
    /// Which route the export takes. The default is <see cref="ExportRoute.Auto"/>, which decides from
    /// what the model holds.
    /// </summary>
    public ExportRoute Route { get; set; } = ExportRoute.Auto;

    /// <summary>
    /// The precision of the exported graph, as the tool spells it: <c>fp32</c> (the default),
    /// <c>fp16</c>, <c>bf16</c>, <c>int8</c> or <c>int4</c> for the GenAI builder. Never
    /// <see langword="null"/>: an unset value reads as <see cref="DefaultPrecision"/>.
    /// </summary>
    /// <remarks>
    /// It is passed to the tool verbatim and the TOOL decides what it accepts; a value it does not know
    /// fails the export with what the tool said. The Optimum route IGNORES it and exports at the
    /// checkpoint's own precision - what was asked for is still recorded in the bundle's provenance, so
    /// that a bundle never claims a precision that was not applied to it.
    /// </remarks>
    public string Precision
    {
        get { return _precision; }
        set { _precision = string.IsNullOrWhiteSpace(value) ? DefaultPrecision : value.Trim(); }
    }

    /// <summary>
    /// The name to store the exported bundle under, or <see langword="null"/> for the default: the
    /// source name with its tag replaced by <c>onnx</c>.
    /// </summary>
    public string OutputName { get; set; }

    /// <summary>
    /// Whether a bundle already stored under the output name is replaced. The default is
    /// <see langword="false"/>, which fails the export instead and names the option.
    /// </summary>
    public bool Overwrite { get; set; }

    /// <summary>
    /// Whether the export may run Python code that the MODEL's publisher ships - a custom tokenizer or
    /// configuration class in the checkpoint's own <c>.py</c> files. The default is
    /// <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Some checkpoints cannot be read at all without it: the tooling refuses to load a tokenizer whose
    /// class is defined in the repository rather than in the transformers library. Setting it to
    /// <see langword="true"/> tells the tooling to import that file, which RUNS THE PUBLISHER'S OWN
    /// PYTHON in this process, with everything that implies. Set it only for a model whose files you
    /// have looked at.
    /// </para>
    /// <para>
    /// It reaches the GenAI builder as its <c>hf_remote</c> extra option and Optimum as its
    /// <c>trust_remote_code</c> argument, and the <see cref="ExportRoute.PublisherOnnx"/> route ignores
    /// it, because that route runs no Python at all.
    /// </para>
    /// </remarks>
    public bool AllowRemoteCode { get; set; }
}
