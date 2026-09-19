using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// How a checkpoint is converted to GGUF: the numeric type to write, the architecture to read it as, what to
/// call the result, and whether an existing model of that name may be replaced.
/// </summary>
public sealed class ConvertOptions
{
    /// <summary>The numeric type to write the weights as. The default keeps the checkpoint's own type.</summary>
    public GgufOutputType OutputType { get; set; } = GgufOutputType.Auto;

    /// <summary>The architecture to read the checkpoint as. The default reads it from the configuration.</summary>
    public CheckpointArchitecture Architecture { get; set; } = CheckpointArchitecture.Auto;

    /// <summary>
    /// The name to store the result under. When it is <see langword="null"/> the source name is used with a
    /// tag that names the conversion.
    /// </summary>
    public string OutputName { get; set; }

    /// <summary>Whether an existing model of the output name may be replaced.</summary>
    public bool Overwrite { get; set; }

    /// <summary>
    /// The model identifier the general metadata is derived from - the publisher's <c>organization/model</c>
    /// string, or anything shaped like it. When it is <see langword="null"/> the checkpoint directory's own name
    /// is used, which is what the inference engine's converter does.
    /// </summary>
    public string ModelId { get; set; }

    /// <summary>
    /// Token contents to treat as ADDED AND SPECIAL, which is what makes the inference engine's own rule write
    /// them as control tokens rather than ordinary ones. <see langword="null"/> - the default - supplies nothing,
    /// and only what the checkpoint's own files declare is read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A conversion reads files and never runs a publisher's Python. A tokenizer class that declares its unknown,
    /// beginning-of-sequence, end-of-sequence or padding token only inside its own <c>.py</c> file therefore
    /// leaves those tokens undeclared on disk, and they are written as ordinary tokens - which is exactly what
    /// the files say they are. Naming them here supplies the declaration the files are missing. The RULE is
    /// unchanged and belongs to the engine; only the missing INPUT is given, so nothing about any particular
    /// model enters this library.
    /// </para>
    /// <para>
    /// It changes the token TYPES and nothing else. The identifiers of the special tokens still come from
    /// <c>config.json</c>, as they always did, so a model still ends generation on the token its configuration
    /// names whether or not anything is supplied here.
    /// </para>
    /// <para>
    /// Every entry must be a token the checkpoint's vocabulary holds. One that is not fails the conversion with
    /// an <see cref="ArgumentException"/> naming it, because a name that matches nothing is a mistake rather
    /// than a request for nothing.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> AddedSpecialTokens { get; set; }
}
