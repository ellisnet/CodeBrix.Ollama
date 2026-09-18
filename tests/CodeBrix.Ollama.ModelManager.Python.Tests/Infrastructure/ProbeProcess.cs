using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace CodeBrix.Ollama.ModelManager.Python.Tests;

/// <summary>
/// Runs the CodeBrix.Ollama.ModelManager.Probe console application and watches whether it ends.
/// </summary>
/// <remarks>
/// <para>
/// Two of the things this repository has to be sure of are process-wide and order-dependent - whether an
/// assembly was ever loaded, and whether a process that started a Python interpreter still exits - so
/// neither can be observed from inside a test run that does other things first. The probe is a whole
/// process per observation, and it is always killed before the helper returns, so a probe that hangs can
/// never hang the suite.
/// </para>
/// <para>
/// Waiting is done by polling on the test's own cancellation token rather than by awaiting, so nothing
/// here moves work to another thread.
/// </para>
/// </remarks>
public static class ProbeProcess
{
    /// <summary>The folder the probe is copied into beside the test assembly.</summary>
    public const string FolderName = "probe";

    /// <summary>The probe's assembly name, which is also its launcher's file name.</summary>
    public const string ProbeName = "CodeBrix.Ollama.ModelManager.Probe";

    /// <summary>
    /// The variable the reduce mode reads the graph to reduce from. It is spelled here rather than
    /// taken from the probe, whose assembly is deliberately not referenced by anything.
    /// </summary>
    public const string ModelVariable = "CODEBRIX_OLLAMA_PROBE_MODEL";

    /// <summary>How often the polling waits look at the world.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Runs one probe mode and waits a bounded time for the process to end.
    /// </summary>
    /// <param name="mode">The mode to run, spelled as the probe's command line spells it.</param>
    /// <param name="exitWait">How long to wait for the process to end.</param>
    /// <param name="environment">Extra environment variables for the child, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The test's cancellation token.</param>
    /// <returns>What happened.</returns>
    public static ProbeRun Run(
        string mode,
        TimeSpan exitWait,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        using Process process = Start(mode, environment);

        bool exited;
        try
        {
            exited = WaitForExit(process, exitWait, cancellationToken);
        }
        finally
        {
            KillIfRunning(process);
        }

        elapsed.Stop();

        //Draining is safe only once the process is gone: a live child's stream would block this thread.
        string output = string.Empty;
        string errors = string.Empty;
        if (process.HasExited)
        {
            output = process.StandardOutput.ReadToEnd();
            errors = process.StandardError.ReadToEnd();
        }

        return new ProbeRun(
            exited,
            exited ? process.ExitCode : -1,
            output,
            errors,
            elapsed.Elapsed.TotalSeconds);
    }

    /// <summary>
    /// Starts one probe, preferring the native launcher and falling back to the <c>dotnet</c> muxer when
    /// there is none or the operating system refuses to execute it.
    /// </summary>
    /// <param name="mode">The mode to run.</param>
    /// <param name="environment">Extra environment variables for the child, or <see langword="null"/>.</param>
    /// <returns>The running process.</returns>
    private static Process Start(string mode, IReadOnlyDictionary<string, string> environment)
    {
        try
        {
            ProcessStartInfo startInfo = CreateStartInfo(mode, environment, useMuxer: false);
            return Process.Start(startInfo)
                   ?? throw new InvalidOperationException($"'{startInfo.FileName}' could not be started.");
        }
        catch (Win32Exception)
        {
            //The launcher is there but not executable - a file mode that did not survive being copied.
            ProcessStartInfo startInfo = CreateStartInfo(mode, environment, useMuxer: true);
            return Process.Start(startInfo)
                   ?? throw new InvalidOperationException($"'{startInfo.FileName}' could not be started.");
        }
    }

    /// <summary>
    /// Builds the command that runs one probe mode.
    /// </summary>
    /// <param name="mode">The mode to pass on the command line.</param>
    /// <param name="environment">Extra environment variables for the child, or <see langword="null"/>.</param>
    /// <param name="useMuxer">Run the assembly through <c>dotnet</c> rather than the launcher.</param>
    /// <returns>The start information.</returns>
    private static ProcessStartInfo CreateStartInfo(
        string mode, IReadOnlyDictionary<string, string> environment, bool useMuxer)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, FolderName);
        string launcher = Path.Combine(
            directory, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ProbeName + ".exe" : ProbeName);
        string assembly = Path.Combine(directory, ProbeName + ".dll");

        if (!File.Exists(assembly))
        {
            throw new InvalidOperationException(
                $"The probe was not found at '{assembly}'. It is copied there by this project's"
                + " CopyProbeToOutput target; build the solution, not just this project.");
        }

        var startInfo = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = directory,
        };

        if (!useMuxer && File.Exists(launcher))
        {
            startInfo.FileName = launcher;
        }
        else
        {
            startInfo.FileName = "dotnet";
            startInfo.ArgumentList.Add(assembly);
        }

        startInfo.ArgumentList.Add(mode);

        if (environment != null)
        {
            foreach (KeyValuePair<string, string> variable in environment)
            {
                startInfo.Environment[variable.Key] = variable.Value;
            }
        }

        return startInfo;
    }

    /// <summary>
    /// Waits a bounded time for the process to end.
    /// </summary>
    /// <param name="process">The running probe.</param>
    /// <param name="exitWait">How long to wait.</param>
    /// <param name="cancellationToken">The test's cancellation token.</param>
    /// <returns><see langword="true"/> when it ended within the wait.</returns>
    private static bool WaitForExit(Process process, TimeSpan exitWait, CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();

        while (!process.HasExited)
        {
            if (elapsed.Elapsed > exitWait)
            {
                return false;
            }

            if (cancellationToken.WaitHandle.WaitOne(PollInterval))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        return true;
    }

    /// <summary>
    /// Kills the probe if it is still running, so that nothing is left behind however a test ends.
    /// </summary>
    /// <param name="process">The probe.</param>
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
        catch (Win32Exception)
        {
            //As above.
        }
    }
}
