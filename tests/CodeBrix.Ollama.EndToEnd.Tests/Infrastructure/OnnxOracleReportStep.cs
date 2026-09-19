namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>One step's entry in the oracle's report.</summary>
public sealed class OnnxOracleReportStep
{
    /// <summary>Which step it is: nought for the first, one for the cached second.</summary>
    public int Step { get; set; }

    /// <summary>How long onnxruntime's own run took.</summary>
    public double Milliseconds { get; set; }
}
