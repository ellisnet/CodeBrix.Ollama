using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>
/// Runs a Python script with the virtual environment's own interpreter, as a child process.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here goes through either library, and that is the point. What these tests need from Python is a
/// SECOND OPINION - the token identifiers the checkpoint's own framework produces, and the file the inference
/// engine's own converter writes - so the reference has to come from outside anything under test. It is what a
/// person would do: spawn the environment's interpreter over a script, and read what it left behind.
/// </para>
/// <para>
/// It knows only which interpreter and which script; running the process is <see cref="ChildProcess"/>'s
/// work, which is also what runs the programs that are not Python. This project carries its own copy of both
/// rather than sharing the Python project's, because no test project references another; this copy can also
/// be pointed at a script outside the project, which is how the engine's own converter is run.
/// </para>
/// </remarks>
public static class VenvPython
{
    /// <summary>The folder this project's scripts are copied into beside the test assembly.</summary>
    public const string FolderName = "Python";

    /// <summary>How long a script is given before the child is killed.</summary>
    public static readonly TimeSpan DefaultTimeout = ChildProcess.DefaultTimeout;

    /// <summary>
    /// The interpreter of the virtual environment the tests were told to use.
    /// </summary>
    /// <param name="virtualEnvironment">The virtual environment's folder.</param>
    /// <returns>The absolute path of its interpreter.</returns>
    public static string Executable(string virtualEnvironment)
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(virtualEnvironment, "Scripts", "python.exe")
            : Path.Combine(virtualEnvironment, "bin", "python");

    /// <summary>
    /// The absolute path of one of this project's scripts, as it was copied to the output.
    /// </summary>
    /// <param name="scriptName">The script's file name.</param>
    /// <returns>The absolute path.</returns>
    public static string ScriptPath(string scriptName)
        => Path.Combine(AppContext.BaseDirectory, FolderName, scriptName);

    /// <summary>
    /// Runs one of this project's own scripts and waits a bounded time for it to finish.
    /// </summary>
    /// <param name="virtualEnvironment">The virtual environment whose interpreter runs it.</param>
    /// <param name="scriptName">The script's file name, as it sits in this project's Python folder.</param>
    /// <param name="arguments">The arguments to pass after the script.</param>
    /// <param name="cancellationToken">The test's cancellation token.</param>
    /// <returns>What the script printed and what it exited with.</returns>
    public static ChildProcessRun Run(
        string virtualEnvironment,
        string scriptName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
        => RunScript(virtualEnvironment, ScriptPath(scriptName), arguments, null, null, cancellationToken);

    /// <summary>
    /// Runs any script with the environment's interpreter and waits a bounded time for it to finish.
    /// </summary>
    /// <param name="virtualEnvironment">The virtual environment whose interpreter runs it.</param>
    /// <param name="scriptPath">The absolute path of the script.</param>
    /// <param name="arguments">The arguments to pass after the script.</param>
    /// <param name="environment">Extra environment variables, or <see langword="null"/> for none.</param>
    /// <param name="workingDirectory">
    /// The directory to run in, or <see langword="null"/> for the script's own folder.
    /// </param>
    /// <param name="cancellationToken">The test's cancellation token.</param>
    /// <returns>What the script printed and what it exited with.</returns>
    /// <exception cref="InvalidOperationException">The interpreter or the script is not there.</exception>
    public static ChildProcessRun RunScript(
        string virtualEnvironment,
        string scriptPath,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        string interpreter = Executable(virtualEnvironment);

        if (!File.Exists(interpreter))
        {
            throw new InvalidOperationException("No interpreter at '" + interpreter + "'.");
        }
        if (!File.Exists(scriptPath))
        {
            throw new InvalidOperationException(
                "The script '" + scriptPath + "' is not there; when it is one of this project's own, check the"
                + " csproj item that copies the Python folder to the output.");
        }

        var command = new List<string> { scriptPath };
        foreach (string argument in arguments ?? Array.Empty<string>())
        {
            command.Add(argument);
        }

        return ChildProcess.Run(
            interpreter, command, environment, workingDirectory ?? Path.GetDirectoryName(scriptPath),
            cancellationToken);
    }
}
