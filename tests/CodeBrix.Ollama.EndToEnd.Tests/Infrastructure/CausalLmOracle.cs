using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>
/// Runs the bundle's own people's answer, in the virtual environment's own interpreter: the publisher's
/// tokenizer for the token numbers, and the runtime the bundle's generation configuration was written for
/// to generate from it.
/// </summary>
/// <remarks>
/// <para>
/// THE REFERENCE HAS TO COME FROM OUTSIDE ANYTHING UNDER TEST, which is why this spawns an interpreter rather
/// than computing anything itself. Nothing here goes through either library.
/// </para>
/// <para>
/// IT DOES TWO THINGS in one run, because spawning the interpreter and importing its libraries costs more
/// than either: it generates greedily from each prompt, and it reports the most likely token at every
/// position of a sequence it is given - which is teacher forcing, the way a quantized variant is compared
/// without letting one different choice send the two runs down different paths.
/// </para>
/// </remarks>
public static class CausalLmOracle
{
    /// <summary>The script's file name in this project's Python folder.</summary>
    public const string ScriptName = "causal_lm_oracle.py";

    /// <summary>Runs the oracle over one bundle.</summary>
    /// <param name="virtualEnvironment">The virtual environment whose interpreter runs the script.</param>
    /// <param name="bundleDirectory">The bundle, laid out as a directory.</param>
    /// <param name="workDirectory">Where the script writes what it produced.</param>
    /// <param name="prompts">The prompts to generate from, or an empty list to generate nothing.</param>
    /// <param name="maximumTokens">How many tokens to generate for each prompt.</param>
    /// <param name="threads">How many threads onnxruntime is given for the teacher forcing.</param>
    /// <param name="sequences">The sequences to report the most likely token at every position of.</param>
    /// <param name="cancellationToken">The test's cancellation token.</param>
    /// <returns>What it wrote.</returns>
    /// <exception cref="InvalidOperationException">The script did not finish, or did not say it succeeded.</exception>
    public static CausalLmOracleRun Run(
        string virtualEnvironment,
        string bundleDirectory,
        string workDirectory,
        IReadOnlyList<string> prompts,
        int maximumTokens,
        int threads,
        IReadOnlyList<IReadOnlyList<int>> sequences,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(workDirectory);
        string report = Path.Combine(workDirectory, "oracle.json");

        List<string> arguments = new List<string>
        {
            "--bundle", bundleDirectory,
            "--out", report,
            "--max", maximumTokens.ToString(CultureInfo.InvariantCulture),
            "--threads", threads.ToString(CultureInfo.InvariantCulture),
        };

        foreach (string prompt in prompts)
        {
            arguments.Add("--prompt");
            arguments.Add(prompt);
        }

        if (sequences != null && sequences.Count > 0)
        {
            string path = Path.Combine(workDirectory, "sequences.json");
            File.WriteAllText(path, JsonSerializer.Serialize(sequences));
            arguments.Add("--teacher-force");
            arguments.Add(path);
        }

        ChildProcessRun run = VenvPython.Run(
            virtualEnvironment, ScriptName, arguments, cancellationToken);

        if (!run.Exited || run.ExitCode != 0
            || !run.Output.Contains("CAUSAL LM ORACLE OK", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The text oracle did not finish: " + run.Report());
        }

        return JsonSerializer.Deserialize<CausalLmOracleRun>(
            File.ReadAllText(report),
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                PropertyNameCaseInsensitive = true,
            });
    }
}
