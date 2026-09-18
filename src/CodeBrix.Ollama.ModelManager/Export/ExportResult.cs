using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// What an export to ONNX produced: the derived bundle's name, the files it holds, and which tool at
/// which version wrote them. The bundle itself is in the store, and
/// <see cref="IModelStore.ShowAsync"/> reports the same provenance from there.
/// </summary>
public sealed class ExportResult
{
    /// <summary>
    /// Initializes a result.
    /// </summary>
    /// <param name="name">The name the derived bundle is stored under.</param>
    /// <param name="files">The relative paths the bundle holds, in manifest order.</param>
    /// <param name="tool">The tool that produced the files.</param>
    /// <param name="toolVersion">That tool's version, as the tool itself reports it.</param>
    /// <param name="routeUsed">The route that was taken, which is never <see cref="ExportRoute.Auto"/>.</param>
    public ExportResult(
        string name,
        IReadOnlyList<string> files,
        string tool,
        string toolVersion,
        ExportRoute routeUsed)
    {
        Name = name;
        Files = files ?? Array.Empty<string>();
        Tool = tool;
        ToolVersion = toolVersion;
        RouteUsed = routeUsed;
    }

    /// <summary>The name the derived bundle is stored under, ready to be shown, resolved or materialized.</summary>
    public string Name { get; }

    /// <summary>The relative paths the bundle holds, in manifest order.</summary>
    public IReadOnlyList<string> Files { get; }

    /// <summary>
    /// The tool that produced the files: "onnxruntime-genai", "optimum", or "publisher" when the files
    /// are the publisher's own and this library only registered them.
    /// </summary>
    public string Tool { get; }

    /// <summary>
    /// The version of <see cref="Tool"/>, as the tool itself reports it, or <see langword="null"/> when
    /// it does not report one.
    /// </summary>
    public string ToolVersion { get; }

    /// <summary>
    /// The route that was taken. It is the route that ran, so it is never
    /// <see cref="ExportRoute.Auto"/>, whatever was asked for.
    /// </summary>
    public ExportRoute RouteUsed { get; }

    /// <summary>
    /// Returns the name, the route and the number of files.
    /// </summary>
    /// <returns>A one-line description of the export.</returns>
    public override string ToString()
    {
        return Name + " (" + RouteUsed + ", " + Files.Count + " files)";
    }
}
