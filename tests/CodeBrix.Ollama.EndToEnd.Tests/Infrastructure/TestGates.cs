using System;

namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>
/// The environment variable NAMES this project's gates are written in, so that a gate is never spelled out in
/// a test file. They are the same variables the other suites use, plus one of this project's own.
/// </summary>
public static class TestGates
{
    /// <summary>Enables the local multi-gigabyte MuseCoco checkpoint staging and generation test.</summary>
    public const string RunMuseCocoTests = "CODEBRIX_OLLAMA_RUN_MUSECOCO_TESTS";

    /// <summary>
    /// The gate every test in this project carries. Nothing here downloads, converts or loads a model unless
    /// it is set to "1".
    /// </summary>
    public const string RunLiveTests = "CODEBRIX_OLLAMA_RUN_LIVE_TESTS";

    /// <summary>
    /// The gate the comparisons against the checkpoint's own framework carry AS WELL, because they spawn the
    /// virtual environment's interpreter over this project's oracle script.
    /// </summary>
    public const string RunPythonTests = "CODEBRIX_OLLAMA_RUN_PYTHON_TESTS";

    /// <summary>
    /// The gate the multi-gigabyte subjects carry AS WELL. It is the same variable the store's own suite uses
    /// for its large repositories, and it is never opened by accident: a run that opens it downloads and
    /// converts about four gigabytes and keeps the checkpoint in the test-model cache afterwards.
    /// </summary>
    public const string RunLargeMusicTests = "CODEBRIX_OLLAMA_RUN_LARGE_MUSIC_TESTS";

    /// <summary>
    /// Not a gate: the virtual environment whose interpreter runs the oracle script. It is the same variable
    /// the library reads to find a CPython of its own, and the same one the Python suite is given.
    /// </summary>
    public const string PythonVirtualEnvironment = "CODEBRIX_OLLAMA_PYTHON_VENV";

    /// <summary>
    /// Not a gate and not a "1": a checkout of the inference engine at the vendored commit, which the
    /// byte-identity test runs the engine's OWN converter out of. It names a folder because what it opens is a
    /// comparison against something that lives outside this repository and can never be checked in.
    /// </summary>
    public const string EngineClone = "CODEBRIX_OLLAMA_ENGINE_CLONE";

    /// <summary>
    /// Not a gate and not a "1": the engine's own command-line quantizer, BUILT by a maintainer out of a
    /// checkout at the vendored commit, which the byte-identity test compares the runner against. It names a
    /// file because what it opens is a comparison against a program that lives outside this repository, is
    /// never checked in and is never built by anything here; MAINTAINER-README records how it is made.
    /// </summary>
    public const string QuantizeTool = "CODEBRIX_OLLAMA_QUANTIZE_TOOL";

    /// <summary>
    /// Not a gate: where the models these tests reuse are kept between runs. Unset, they go under the local
    /// application data folder, which is where the other suites' cache lives too.
    /// </summary>
    public const string TestModelDirectory = "CODEBRIX_OLLAMA_TEST_MODEL_DIR";

    /// <summary>
    /// Not a gate and not a "1": a checkout of the MIDI model publisher's own repository, which the
    /// generation tests run the publisher's own loop out of. It names a folder because what it opens is a
    /// comparison against something that lives outside this repository and can never be checked in.
    /// </summary>
    public const string MidiModelClone = "CODEBRIX_OLLAMA_SKYTNT_CLONE";

    /// <summary>
    /// Not a gate and not a "1": a folder to leave the generated music in, so that a maintainer can LISTEN to
    /// what a run produced. Unset, nothing is written outside the test's own temporary folder.
    /// </summary>
    public const string MidiOutputDirectory = "CODEBRIX_OLLAMA_MIDI_OUTPUT_DIR";

    /// <summary>
    /// The virtual environment the tests were told to use.
    /// </summary>
    /// <returns>The folder it names.</returns>
    /// <exception cref="InvalidOperationException">The variable is not set.</exception>
    public static string RequireVirtualEnvironment()
    {
        string value = Environment.GetEnvironmentVariable(PythonVirtualEnvironment);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                "Set " + PythonVirtualEnvironment + " to the virtual environment holding torch and"
                + " transformers; it is what this project's oracle script is run with.");
        }

        return value.Trim();
    }

    /// <summary>
    /// The engine checkout the byte-identity test converts with.
    /// </summary>
    /// <returns>The folder it names.</returns>
    /// <exception cref="InvalidOperationException">The variable is not set.</exception>
    public static string RequireEngineClone()
    {
        string value = Environment.GetEnvironmentVariable(EngineClone);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                "Set " + EngineClone + " to a checkout of the inference engine at the vendored commit.");
        }

        return value.Trim();
    }

    /// <summary>
    /// The MIDI model publisher's own repository, which the generation tests run its own loop out of.
    /// </summary>
    /// <returns>The folder it names.</returns>
    /// <exception cref="InvalidOperationException">The variable is not set.</exception>
    public static string RequireMidiModelClone()
    {
        string value = Environment.GetEnvironmentVariable(MidiModelClone);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                "Set " + MidiModelClone + " to a checkout of the MIDI model publisher's repository at the"
                + " commit MAINTAINER-README records.");
        }

        return value.Trim();
    }

    /// <summary>
    /// Where to leave the generated music, when a maintainer asked for it to be left anywhere.
    /// </summary>
    /// <returns>The folder it names, or <see langword="null"/> when nothing is to be left.</returns>
    public static string MusicOutputDirectory()
    {
        string value = Environment.GetEnvironmentVariable(MidiOutputDirectory);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// The engine's own quantizer the byte-identity test compares against.
    /// </summary>
    /// <returns>The file it names.</returns>
    /// <exception cref="InvalidOperationException">The variable is not set.</exception>
    public static string RequireQuantizeTool()
    {
        string value = Environment.GetEnvironmentVariable(QuantizeTool);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                "Set " + QuantizeTool + " to the engine's quantizer, built out of a checkout at the vendored"
                + " commit; MAINTAINER-README records the commands.");
        }

        return value.Trim();
    }
}
