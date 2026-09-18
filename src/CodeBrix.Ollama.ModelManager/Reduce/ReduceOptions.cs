using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// What a reduction is asked to do: which mode, with what block-wise settings, through which engine,
/// over which files, under what name, and whether it may replace a bundle that is already there.
/// </summary>
/// <remarks>
/// <para>
/// Every property has a value that is safe when nothing is said, so an instance built with an object
/// initializer that sets only what it cares about is always usable. The defaults are ONNX Runtime's own
/// defaults for the tools behind each mode, so a reduction that names only a mode produces what that
/// tool produces when it is run with nothing but a mode.
/// </para>
/// </remarks>
public sealed class ReduceOptions
{
    private int _blockSize = DefaultBlockSize;

    /// <summary>
    /// The number of weight values that share one scale in the block-wise modes when the caller names
    /// none. It is the tooling's own default.
    /// </summary>
    public const int DefaultBlockSize = 128;

    /// <summary>
    /// What the reduction does. The default is <see cref="ReduceMode.DynamicInt8"/>.
    /// </summary>
    public ReduceMode Mode { get; set; } = ReduceMode.DynamicInt8;

    /// <summary>
    /// How many weight values share one scale in the two weight-only modes. The default is
    /// <see cref="DefaultBlockSize"/>; smaller blocks cost more scales and keep more accuracy.
    /// </summary>
    /// <remarks>
    /// The runtimes that implement the block-wise operator implement a fixed set of block sizes - 16, 32,
    /// 64, 128 and 256 - so a graph quantized with anything else is written, but may not be one that a
    /// runtime will load. <see cref="ReduceMode.DynamicInt8"/> ignores this entirely.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not greater than zero.</exception>
    public int BlockSize
    {
        get { return _blockSize; }
        set
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "A block size must be greater than zero.");
            }
            _blockSize = value;
        }
    }

    /// <summary>
    /// Whether the two weight-only modes quantize each block symmetrically - one scale and no zero point
    /// - rather than asymmetrically. The default is <see langword="false"/>, which is the tooling's own
    /// default and keeps a zero point per block.
    /// </summary>
    public bool IsSymmetric { get; set; }

    /// <summary>
    /// The accuracy level a runtime is asked to compute the block-wise matrix multiply at, or
    /// <see langword="null"/> - the default - to leave the choice to the runtime. It is written into the
    /// graph as an attribute and means nothing to the file's size.
    /// </summary>
    public int? AccuracyLevel { get; set; }

    /// <summary>
    /// Whether the graph is prepared - shape inference and the basic optimizations - before it is
    /// quantized. The default is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// It applies to <see cref="ReduceMode.DynamicInt8"/>, which quantizes what the shapes tell it to
    /// quantize and warns when it is handed a graph nobody prepared. The weight-only modes read the
    /// weights themselves and need nothing inferred, so they never preprocess whatever this says; a
    /// graph prepared on purpose for them is <see cref="ReduceMode.PreprocessOnly"/>, whose output is an
    /// ordinary bundle that can be reduced afterwards.
    /// </remarks>
    public bool Preprocess { get; set; } = true;

    /// <summary>
    /// The name to store the reduced bundle under, or <see langword="null"/> for the default: the source
    /// name with the mode's tag added to its own.
    /// </summary>
    public string OutputName { get; set; }

    /// <summary>
    /// Whether a bundle already stored under the output name is replaced. The default is
    /// <see langword="false"/>, which fails the reduction instead and names the option.
    /// </summary>
    public bool Overwrite { get; set; }

    /// <summary>
    /// Which engine does the work. The default is <see cref="ReduceEngine.Auto"/>.
    /// </summary>
    public ReduceEngine Engine { get; set; } = ReduceEngine.Auto;

    /// <summary>
    /// The files to reduce, as the bundle spells their paths, or <see langword="null"/> - the default -
    /// for every <c>.onnx</c> file the bundle holds.
    /// </summary>
    /// <remarks>
    /// Naming files reduces those and carries the rest of the bundle through unchanged, which is how a
    /// bundle that ships several graphs can have one of them reduced and stay complete.
    /// </remarks>
    public IReadOnlyList<string> Files { get; set; }
}
