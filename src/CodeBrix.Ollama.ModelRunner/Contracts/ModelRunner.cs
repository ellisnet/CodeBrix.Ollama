using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Ollama.Core;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The entry point: loads a model file into an <see cref="IRunningModel"/>, probes a model file without
/// loading its weights, rewrites a model file at another quantization, and reports on the native engine.
/// </summary>
public static class ModelRunner
{
    /// <summary>
    /// Loads a model and returns the contract to query it through. Dispose the result to unload the model.
    /// </summary>
    /// <param name="options">Which model to load and how.</param>
    /// <param name="cancellationToken">A token to cancel the load.</param>
    /// <returns>The running model.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><see cref="ModelRunnerOptions.ModelPath"/> is not set.</exception>
    /// <exception cref="NativeLibraryException">The native engine could not be loaded.</exception>
    /// <exception cref="ModelLoadException">The model could not be loaded.</exception>
    public static Task<IRunningModel> LoadAsync(ModelRunnerOptions options, CancellationToken cancellationToken = default)
    {
        RequireCoreContract();
        return ModelEngine.LoadAsync(options, cancellationToken);
    }

    /// <summary>
    /// Reads what the engine knows about a model file without loading its weights: architecture, parameter
    /// count, the memory the weights need, the training context and the embedded chat template.
    /// </summary>
    /// <param name="modelPath">The path of the model's GGUF file.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The details.</returns>
    /// <exception cref="NativeLibraryException">The native engine could not be loaded.</exception>
    /// <exception cref="ModelLoadException">The file could not be read as a model.</exception>
    public static Task<ModelDetails> ProbeAsync(string modelPath, CancellationToken cancellationToken = default)
    {
        RequireCoreContract();
        return ModelEngine.ProbeAsync(modelPath, cancellationToken);
    }

    /// <summary>
    /// Rewrites a GGUF model at another quantization - a smaller file that loads faster, needs less memory
    /// and generates with a little more error - and reports what it read and wrote.
    /// </summary>
    /// <remarks>
    /// <para>
    /// IT NEEDS NOTHING INSTALLED. The quantizer is the native engine that ships inside this package, the
    /// same one that runs a model, so there is no tool to fetch, no Python and no second copy of anything.
    /// With no <paramref name="options"/> the call reproduces, byte for byte, what the engine's own
    /// command-line quantizer writes for the same file and the same type.
    /// </para>
    /// <para>
    /// IT IS SLOW AND IT READS THE WHOLE MODEL. Quantizing is minutes of work on a model of any size and
    /// writes a second file beside the first, so the directory <paramref name="outputPath"/> names must have
    /// room for it. Nothing is streamed to the caller and there is no progress report; what the engine has to
    /// say goes to the handler <see cref="SetLogHandler"/> installed, which is where a caller who wants to
    /// watch a long quantization should look.
    /// </para>
    /// <para>
    /// CANCELLATION IS HONOURED BEFORE THE ENGINE STARTS AND NOT AFTER. The native quantizer offers no way to
    /// interrupt it, so a token cancelled while it is running is noticed only when it returns: the call runs
    /// to the end and the file is written. A caller who has to be able to stop should quantize in a process
    /// of its own.
    /// </para>
    /// <para>
    /// NO PARTIAL FILE IS EVER LEFT AT <paramref name="outputPath"/>. The engine writes into a file beside it
    /// and the finished file is moved into place; a failure removes what was written and leaves a file that
    /// was already at that path untouched.
    /// </para>
    /// </remarks>
    /// <param name="inputPath">The path of the GGUF file to read. It is never modified.</param>
    /// <param name="outputPath">
    /// The path of the GGUF file to write. Its directory must exist; a file already there is replaced when
    /// the quantization succeeds.
    /// </param>
    /// <param name="type">The file type to write.</param>
    /// <param name="options">
    /// How many threads to use and the two engine switches worth exposing, or <see langword="null"/> - the
    /// default - for the engine's own defaults.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation before the engine starts.</param>
    /// <returns>The two paths, the type written, the two sizes and how long the engine took.</returns>
    /// <exception cref="ArgumentException">
    /// A path is not set, or the two paths name one file.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="type"/> is not one of the engine's file types.
    /// </exception>
    /// <exception cref="FileNotFoundException">There is no file at <paramref name="inputPath"/>.</exception>
    /// <exception cref="DirectoryNotFoundException">The directory of <paramref name="outputPath"/> does not exist.</exception>
    /// <exception cref="NativeLibraryException">The native engine could not be loaded.</exception>
    /// <exception cref="ModelLoadException">
    /// The engine would not quantize the file: it is not a GGUF model, its weights are already quantized and
    /// <see cref="QuantizeOptions.AllowRequantize"/> is not set, or it could not be written. The engine's own
    /// last words are on the exception's message.
    /// </exception>
    public static Task<QuantizeResult> QuantizeAsync(
        string inputPath,
        string outputPath,
        GgufQuantizationType type,
        QuantizeOptions options = null,
        CancellationToken cancellationToken = default)
    {
        return ModelQuantizer.QuantizeAsync(inputPath, outputPath, type, options, cancellationToken);
    }

    /// <summary>
    /// Loads the native engine if it is not loaded already and reports on it: where it was loaded from, what
    /// build it is, which CPU features and devices it sees.
    /// </summary>
    /// <returns>The information.</returns>
    /// <exception cref="NativeLibraryException">The native engine could not be loaded.</exception>
    public static NativeRuntimeInfo GetNativeRuntimeInfo()
    {
        return NativeRuntime.Describe();
    }

    /// <summary>
    /// Routes the native engine's log output to a handler. <see langword="null"/> discards it, which is the
    /// default. The handler is called on the engine's threads and must return quickly.
    /// </summary>
    /// <param name="handler">The handler, receiving a severity and one line of text.</param>
    public static void SetLogHandler(Action<ModelRunnerLogLevel, string> handler)
    {
        NativeLog.SetHandler(handler);
    }

    //The two doors a MODEL comes through are where this library reaches CodeBrix.Ollama.Core, so they are
    //where an application that installed mismatched CodeBrix.Ollama packages is told so, in a sentence of
    //its own rather than through a missing member somewhere further in. The check is an integer comparison,
    //and CoreContract.Revision is a constant this assembly's compiler baked in. QuantizeAsync,
    //GetNativeRuntimeInfo and SetLogHandler are not guarded: they never touch the shared code, whatever it
    //grows into, because they talk only to the native engine.
    private static void RequireCoreContract() =>
        CoreContract.Require(CoreContract.Revision, "CodeBrix.Ollama.ModelRunner");
}
