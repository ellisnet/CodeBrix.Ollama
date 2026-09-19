using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>
/// Runs a program in a child process and waits a bounded time for it to finish, collecting what it printed.
/// </summary>
/// <remarks>
/// Nothing here goes through either library, and that is the point. What these tests need from another
/// program is a SECOND OPINION - the token identifiers the checkpoint's own framework produces, the file the
/// inference engine's own converter writes, the file its own quantizer writes - so the reference has to come
/// from outside anything under test. It is what a person would do: run the program and read what it left
/// behind.
/// </remarks>
public static class ChildProcess
{
    /// <summary>How long a program is given before the child is killed.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Runs a program and waits for it.
    /// </summary>
    /// <param name="executable">The absolute path of the program.</param>
    /// <param name="arguments">The arguments to pass it.</param>
    /// <param name="environment">Extra environment variables, or <see langword="null"/> for none.</param>
    /// <param name="workingDirectory">
    /// The directory to run in, or <see langword="null"/> for the program's own folder.
    /// </param>
    /// <param name="cancellationToken">The test's cancellation token.</param>
    /// <returns>What it printed and what it exited with.</returns>
    /// <exception cref="InvalidOperationException">The program is not there, or would not start.</exception>
    public static ChildProcessRun Run(
        string executable,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(executable))
        {
            throw new InvalidOperationException("There is no program at '" + executable + "'.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(executable),
        };
        foreach (string argument in arguments ?? Array.Empty<string>())
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environment != null)
        {
            foreach (KeyValuePair<string, string> variable in environment)
            {
                startInfo.Environment[variable.Key] = variable.Value;
            }
        }

        var output = new StringBuilder();
        var errors = new StringBuilder();
        var elapsed = Stopwatch.StartNew();

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("'" + executable + "' could not be started.");

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
            exited = WaitForExit(process, DefaultTimeout, cancellationToken);
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
                return new ChildProcessRun(
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
    /// <param name="process">The running program.</param>
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
