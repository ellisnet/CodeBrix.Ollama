using System;

namespace CodeBrix.Ollama.ModelManager.Python.Tests;

/// <summary>
/// The environment variable NAMES this project's gate is written in, so that a gate is never spelled out
/// in a test file.
/// </summary>
public static class TestGates
{
    /// <summary>
    /// The gate every test in this project carries. Nothing here runs, and no interpreter is started,
    /// unless it is set to "1".
    /// </summary>
    public const string RunPythonTests = "CODEBRIX_OLLAMA_RUN_PYTHON_TESTS";

    /// <summary>
    /// Not a gate: the virtual environment the tests expect their modules to be installed in. It is the
    /// same variable the library itself reads, and the tests deliberately do NOT pass it as an option, so
    /// that the environment-variable branch of the resolution order is the one being exercised.
    /// </summary>
    public const string PythonVirtualEnvironment = "CODEBRIX_OLLAMA_PYTHON_VENV";

    /// <summary>
    /// The gate the export tests carry AS WELL, because they download the models they export from. It is
    /// the same variable the live tests in the other projects use.
    /// </summary>
    public const string RunLiveTests = "CODEBRIX_OLLAMA_RUN_LIVE_TESTS";

    /// <summary>
    /// Not a gate: where the models the export tests download are kept between runs. Unset, they go
    /// under the local application data folder, which is where the other suites' cache lives too.
    /// </summary>
    public const string TestModelDirectory = "CODEBRIX_OLLAMA_TEST_MODEL_DIR";

    /// <summary>
    /// Whether the gate is open, which is what keeps the fixture from starting an interpreter in a run
    /// where every test is going to skip.
    /// </summary>
    public static bool IsOpen
        => string.Equals(Environment.GetEnvironmentVariable(RunPythonTests), "1", StringComparison.Ordinal);
}
