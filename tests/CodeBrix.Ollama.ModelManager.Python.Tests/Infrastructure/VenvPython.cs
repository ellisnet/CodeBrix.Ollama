using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace CodeBrix.Ollama.ModelManager.Python.Tests;

/// <summary>
/// Runs a TEST-SIDE Python script with the virtual environment's own interpreter, as a child process.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here goes through the library, and that is the point. The library embeds an interpreter to
/// run the tools it ships scripts for; checking what a reduced model COMPUTES means running the model,
/// which the library never does and never will. So the check is what a person would do - spawn the
/// environment's interpreter over a script in this project - and the test reads what it printed.
/// </para>
/// <para>
/// It also keeps the assembly's one embedded interpreter out of the way: a script run in a child process
/// cannot disturb it, whatever it imports.
/// </para>
/// </remarks>
public static class VenvPython
{
    /// <summary>The folder the test-side scripts are copied into beside the test assembly.</summary>
    public const string FolderName = "Python";

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
    /// Runs one script and waits a bounded time for it to finish.
    /// </summary>
    /// <param name="virtualEnvironment">The virtual environment whose interpreter runs it.</param>
    /// <param name="scriptName">The script's file name, as it sits in this project's Python folder.</param>
    /// <param name="arguments">The arguments to pass after the script.</param>
    /// <param name="timeout">How long to wait before the child is killed.</param>
    /// <param name="cancellationToken">The test's cancellation token.</param>
    /// <returns>What the script printed and what it exited with.</returns>
    /// <exception cref="InvalidOperationException">The interpreter or the script is not there.</exception>
    public static VenvPythonRun Run(
        string virtualEnvironment,
        string scriptName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        string interpreter = Executable(virtualEnvironment);
        string script = ScriptPath(scriptName);

        if (!File.Exists(interpreter))
        {
            throw new InvalidOperationException("No interpreter at '" + interpreter + "'.");
        }
        if (!File.Exists(script))
        {
            throw new InvalidOperationException(
                "The script '" + script + "' was not copied to the test output; check the csproj item"
                + " that copies this project's Python folder.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = interpreter,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(script),
        };
        startInfo.ArgumentList.Add(script);
        foreach (string argument in arguments ?? Array.Empty<string>())
        {
            startInfo.ArgumentList.Add(argument);
        }

        var output = new StringBuilder();
        var errors = new StringBuilder();
        var elapsed = Stopwatch.StartNew();

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("'" + interpreter + "' could not be started.");

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                lock (output)
                {
                    output.AppendLine(e.Data);
                }
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                lock (errors)
                {
                    errors.AppendLine(e.Data);
                }
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        bool exited;
        try
        {
            exited = WaitForExit(process, timeout, cancellationToken);
        }
        finally
        {
            KillIfRunning(process);
        }

        elapsed.Stop();

        lock (output)
        {
            lock (errors)
            {
                return new VenvPythonRun(
                    exited,
                    exited ? process.ExitCode : -1,
                    output.ToString(),
                    errors.ToString(),
                    elapsed.Elapsed.TotalSeconds);
            }
        }
    }

    /// <summary>
    /// Waits a bounded time for the child to end, polling on the test's own token.
    /// </summary>
    /// <param name="process">The running script.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <param name="cancellationToken">The test's cancellation token.</param>
    /// <returns><see langword="true"/> when it ended within the wait.</returns>
    private static bool WaitForExit(Process process, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();

        while (!process.HasExited)
        {
            if (elapsed.Elapsed > timeout)
            {
                return false;
            }

            if (cancellationToken.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(100)))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        //Lets the redirected streams finish arriving before anything reads them.
        process.WaitForExit();
        return true;
    }

    /// <summary>
    /// Kills the child if it is still running, so nothing is left behind however a test ends.
    /// </summary>
    /// <param name="process">The child.</param>
    private static void KillIfRunning(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
            //A process that has already gone needs no killing.
        }
        catch (NotSupportedException)
        {
            //As above.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            //As above.
        }
    }
}
