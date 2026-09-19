namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>The <c>oracle.json</c> the script writes: how many threads it used and how long each step took.</summary>
public sealed class OnnxOracleReport
{
    /// <summary>Which of the two SkyTNT graphs was run.</summary>
    public string Kind { get; set; }

    /// <summary>How many threads onnxruntime was given.</summary>
    public int Threads { get; set; }

    /// <summary>One entry per step.</summary>
    public OnnxOracleReportStep[] Steps { get; set; }
}
