namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The environment variables that open the live tests, and where they cache what they download.
/// </summary>
public static class TestGates
{
    /// <summary>Set to "1" to run the live tests that download SmolLM 360M and really generate. Shared with the ModelManager tests.</summary>
    public const string LiveTests = "CODEBRIX_OLLAMA_RUN_LIVE_TESTS";

    /// <summary>Set to "1" (together with <see cref="LiveTests"/>) to run the Qwen 3.5 35B-A3B test, which downloads about 20 GB.</summary>
    public const string Qwen35Tests = "CODEBRIX_OLLAMA_RUN_QWEN35_TESTS";

    /// <summary>Names the directory downloaded models are cached in between runs. Unset: a folder under the user's local application data.</summary>
    public const string ModelCacheDirectory = "CODEBRIX_OLLAMA_TEST_MODEL_DIR";
}
