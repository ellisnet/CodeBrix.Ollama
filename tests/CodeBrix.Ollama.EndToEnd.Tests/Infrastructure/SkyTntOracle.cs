using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>
/// Runs the publisher's own generation loop, in the virtual environment's own interpreter, over a checkout of
/// the publisher's own repository, and reads back every event it wrote.
/// </summary>
/// <remarks>
/// <para>
/// THE REFERENCE HAS TO COME FROM OUTSIDE ANYTHING UNDER TEST, which is why this spawns an interpreter rather
/// than computing anything itself: what the managed driver is held to is the music the publisher's code
/// writes, event for event and parameter for parameter.
/// </para>
/// <para>
/// THE CHECKOUT IS NOT IN THIS REPOSITORY and never will be. The test that uses this is gated on an
/// environment variable naming one, and skips when it is not set.
/// </para>
/// </remarks>
public static class SkyTntOracle
{
    /// <summary>The script's file name in this project's Python folder.</summary>
    public const string ScriptName = "skytnt_generate_oracle.py";

    /// <summary>Runs one generation and reads back what it wrote.</summary>
    /// <param name="virtualEnvironment">The virtual environment whose interpreter runs the script.</param>
    /// <param name="clone">A checkout of the publisher's repository.</param>
    /// <param name="files">The bundle's files: its own names against the paths they are really at.</param>
    /// <param name="workDirectory">Where the script writes what it produced.</param>
    /// <param name="events">How many events to write.</param>
    /// <param name="threads">How many threads onnxruntime is given.</param>
    /// <param name="settings">The settings, as the JSON the script takes.</param>
    /// <param name="promptMidiPath">A piece to continue, or <see langword="null"/> to start from nothing.</param>
    /// <param name="cancellationToken">The test's cancellation token.</param>
    /// <returns>What it wrote.</returns>
    /// <exception cref="InvalidOperationException">The script did not finish, or did not say it succeeded.</exception>
    public static SkyTntOracleRun Run(
        string virtualEnvironment,
        string clone,
        IReadOnlyDictionary<string, string> files,
        string workDirectory,
        int events,
        int threads,
        string settings,
        string promptMidiPath,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(workDirectory);

        List<string> arguments = new List<string>
        {
            "--clone", clone,
            "--config", files["config.json"],
            "--base", files["onnx/model_base.onnx"],
            "--token", files["onnx/model_token.onnx"],
            "--work", workDirectory,
            "--events", events.ToString(CultureInfo.InvariantCulture),
            "--threads", threads.ToString(CultureInfo.InvariantCulture),
            "--settings", settings,
        };

        if (!string.IsNullOrEmpty(promptMidiPath))
        {
            arguments.Add("--prompt-midi");
            arguments.Add(promptMidiPath);
        }

        ChildProcessRun run = VenvPython.Run(
            virtualEnvironment, ScriptName, arguments, cancellationToken);

        if (!run.Exited || run.ExitCode != 0
            || !run.Output.Contains("SKYTNT ORACLE OK", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The MIDI oracle did not finish: " + run.Report());
        }

        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(workDirectory, "oracle.json")));
        JsonElement root = document.RootElement;

        List<IReadOnlyList<int>> promptRows = new List<IReadOnlyList<int>>();
        foreach (JsonElement row in root.GetProperty("prompt_rows").EnumerateArray())
        {
            promptRows.Add(Numbers(row));
        }

        List<SkyTntOracleEvent> written = new List<SkyTntOracleEvent>();
        foreach (JsonElement one in root.GetProperty("events").EnumerateArray())
        {
            List<int> tokens = Numbers(one.GetProperty("tokens"));
            JsonElement decoded = one.GetProperty("event");
            string name = null;
            List<int> values = new List<int>();
            bool first = true;
            foreach (JsonElement part in decoded.EnumerateArray())
            {
                if (first)
                {
                    name = part.GetString();
                    first = false;
                    continue;
                }

                values.Add(part.GetInt32());
            }

            written.Add(new SkyTntOracleEvent(tokens, name, values));
        }

        return new SkyTntOracleRun(
            root.GetProperty("prompt_beats").GetInt32(),
            promptRows,
            written,
            root.GetProperty("elapsed_s").GetDouble(),
            Path.Combine(workDirectory, "generated.mid"));
    }

    private static List<int> Numbers(JsonElement array)
    {
        List<int> numbers = new List<int>();
        foreach (JsonElement one in array.EnumerateArray()) numbers.Add(one.GetInt32());
        return numbers;
    }
}
