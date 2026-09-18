using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// What a reduction produced: the derived bundle's name, the files it holds, which engine ran in which
/// mode, how much smaller the graphs became, and which tool at which version did it. The bundle itself is
/// in the store, and <see cref="IModelStore.ShowAsync"/> reports the same provenance from there.
/// </summary>
public sealed class ReduceResult
{
    /// <summary>
    /// Initializes a result.
    /// </summary>
    /// <param name="name">The name the derived bundle is stored under.</param>
    /// <param name="files">The relative paths the bundle holds, in manifest order.</param>
    /// <param name="engineUsed">The engine that ran, which is never <see cref="ReduceEngine.Auto"/>.</param>
    /// <param name="mode">The mode that ran.</param>
    /// <param name="sourceBytes">The size of the graphs that were reduced, before.</param>
    /// <param name="reducedBytes">The size of what replaced them.</param>
    /// <param name="tool">The tool that did the arithmetic.</param>
    /// <param name="toolVersion">That tool's version, as the tool itself reports it.</param>
    public ReduceResult(
        string name,
        IReadOnlyList<string> files,
        ReduceEngine engineUsed,
        ReduceMode mode,
        long sourceBytes,
        long reducedBytes,
        string tool,
        string toolVersion)
    {
        Name = name;
        Files = files ?? Array.Empty<string>();
        EngineUsed = engineUsed;
        Mode = mode;
        SourceBytes = sourceBytes;
        ReducedBytes = reducedBytes;
        Tool = tool;
        ToolVersion = toolVersion;
    }

    /// <summary>The name the derived bundle is stored under, ready to be shown, resolved or materialized.</summary>
    public string Name { get; }

    /// <summary>The relative paths the bundle holds, in manifest order.</summary>
    public IReadOnlyList<string> Files { get; }

    /// <summary>
    /// The engine that ran. It is the engine that did the work, so it is never
    /// <see cref="ReduceEngine.Auto"/>, whatever was asked for.
    /// </summary>
    public ReduceEngine EngineUsed { get; }

    /// <summary>The mode that ran.</summary>
    public ReduceMode Mode { get; }

    /// <summary>
    /// The total size, in bytes, of the graphs that were reduced AND their external-data files, as they
    /// were before. It is about those files only: a bundle's other files are carried through and count
    /// towards neither number.
    /// </summary>
    public long SourceBytes { get; }

    /// <summary>
    /// The total size, in bytes, of what replaced them - each reduced graph and any external-data file
    /// written beside it.
    /// </summary>
    public long ReducedBytes { get; }

    /// <summary>The tool that did the arithmetic, which is "onnxruntime" for both engines.</summary>
    public string Tool { get; }

    /// <summary>
    /// The version of <see cref="Tool"/>, as the tool itself reports it, or <see langword="null"/> when
    /// it does not report one.
    /// </summary>
    public string ToolVersion { get; }

    /// <summary>
    /// Returns the name, the mode, the engine and how much smaller the graphs became.
    /// </summary>
    /// <returns>A one-line description of the reduction.</returns>
    public override string ToString()
    {
        string ratio = SourceBytes > 0 && ReducedBytes > 0
            ? string.Format(
                CultureInfo.InvariantCulture, ", {0:F2}x smaller", SourceBytes / (double)ReducedBytes)
            : string.Empty;

        return Name + " (" + Mode + ", " + EngineUsed + ratio + ")";
    }
}
