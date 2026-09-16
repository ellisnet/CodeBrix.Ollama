using System;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The entry point: loads a model file into an <see cref="IRunningModel"/>, probes a model file without
/// loading its weights, and reports on the native engine.
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
        return ModelEngine.ProbeAsync(modelPath, cancellationToken);
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
}
