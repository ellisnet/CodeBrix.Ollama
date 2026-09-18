using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// What one graph's trip through a reduction engine produced: which tool ran, at which version, the
/// absolute paths it wrote, and how many bytes they add up to.
/// </summary>
internal sealed class OnnxReduceRun
{
    /// <summary>
    /// Initializes a run result.
    /// </summary>
    /// <param name="tool">The tool that ran.</param>
    /// <param name="toolVersion">That tool's version, as the tool reports it.</param>
    /// <param name="files">The absolute paths the tool wrote, in the order it reported them.</param>
    /// <param name="totalBytes">The total size of those files in bytes.</param>
    internal OnnxReduceRun(string tool, string toolVersion, IReadOnlyList<string> files, long totalBytes)
    {
        Tool = tool;
        ToolVersion = toolVersion;
        Files = files ?? Array.Empty<string>();
        TotalBytes = totalBytes;
    }

    /// <summary>The tool that ran.</summary>
    internal string Tool { get; }

    /// <summary>That tool's version, or <see langword="null"/> when it reports none.</summary>
    internal string ToolVersion { get; }

    /// <summary>The absolute paths the tool wrote: the reduced graph and any file of weights beside it.</summary>
    internal IReadOnlyList<string> Files { get; }

    /// <summary>The total size of those files in bytes.</summary>
    internal long TotalBytes { get; }
}
