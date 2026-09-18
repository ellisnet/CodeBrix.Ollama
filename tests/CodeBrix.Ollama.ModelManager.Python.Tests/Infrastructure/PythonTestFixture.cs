using System;

namespace CodeBrix.Ollama.ModelManager.Python.Tests;

/// <summary>
/// The one interpreter this assembly ever has. It is started once, by the same call a real feature would
/// make, and shut down exactly once when the whole assembly is finished.
/// </summary>
/// <remarks>
/// <para>
/// The constructor does NOTHING when the gate is closed: a run where every test skips must start no
/// interpreter, load no modules and end as quickly as any other offline run.
/// </para>
/// <para>
/// The virtual environment is deliberately NOT passed as an option. It is left to the
/// CODEBRIX_OLLAMA_PYTHON_VENV environment variable, so the environment-variable branch of the resolution
/// order is the branch these tests exercise; the branch that takes it from code is exercised by the probe
/// modes, which name it outright.
/// </para>
/// </remarks>
public sealed class PythonTestFixture : IDisposable
{
    /// <summary>Whether this fixture is the one that started the interpreter.</summary>
    private readonly bool _started;

    /// <summary>Whether the interpreter has been shut down.</summary>
    private bool _isDisposed;

    /// <summary>
    /// Starts the interpreter through the library's own probe, once, when the gate is open.
    /// </summary>
    public PythonTestFixture()
    {
        if (!TestGates.IsOpen)
        {
            return;
        }

        VirtualEnvironment = Environment.GetEnvironmentVariable(TestGates.PythonVirtualEnvironment);
        Options = new PythonOptions();
        Report = PythonSupport.Check(Options, "onnx", "onnxruntime");
        _started = PythonSupport.IsInitialized;
    }

    /// <summary>
    /// The options every test in this assembly runs with: all defaults, so that the environment answers.
    /// </summary>
    public PythonOptions Options { get; }

    /// <summary>
    /// What the one <c>Check</c> this assembly makes reported. <see langword="null"/> when the gate is
    /// closed.
    /// </summary>
    public PythonSupportReport Report { get; }

    /// <summary>
    /// The virtual environment the tests expect, as the environment variable names it.
    /// <see langword="null"/> when the gate is closed.
    /// </summary>
    public string VirtualEnvironment { get; }

    /// <summary>
    /// Ends Python for this process, exactly once, after the last test in the assembly.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        if (_started)
        {
            PythonSupport.Shutdown();
        }
    }
}
