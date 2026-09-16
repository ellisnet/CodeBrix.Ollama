using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// What <see cref="ModelRunner.LoadAsync"/> and <see cref="ModelRunner.ProbeAsync"/> do: the entry into the
/// engine, kept out of the public contract file so the contract stays a contract.
/// </summary>
internal static class ModelEngine
{
    /// <summary>Loads a model and hands back the running model around it.</summary>
    /// <param name="options">Which model to load and how.</param>
    /// <param name="cancellationToken">A token to cancel the load.</param>
    /// <returns>The running model.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">An option is out of range.</exception>
    /// <exception cref="NativeLibraryException">The native engine could not be loaded.</exception>
    /// <exception cref="ModelLoadException">The model could not be loaded.</exception>
    public static async Task<IRunningModel> LoadAsync(
        ModelRunnerOptions options, CancellationToken cancellationToken = default)
    {
        ParameterMapper.Validate(options);

        NativeLibraryLoader.EnsureLoaded();
        EngineLog.Arm();

        EngineWorker worker = new EngineWorker("CodeBrix.Ollama model");

        try
        {
            return await worker.RunAsync(() => RunningModel.LoadOnWorker(worker, options, cancellationToken))
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            worker.Dispose();
            throw;
        }
    }

    /// <summary>Reads what the engine knows about a model file without loading its weights.</summary>
    /// <param name="modelPath">The path of the model's GGUF file.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The details.</returns>
    /// <exception cref="ArgumentException"><paramref name="modelPath"/> is not set.</exception>
    /// <exception cref="NativeLibraryException">The native engine could not be loaded.</exception>
    /// <exception cref="ModelLoadException">The file could not be read as a model.</exception>
    /// <remarks>
    /// The method is asynchronous all the way down on purpose: a caller that holds on to the task rather
    /// than awaiting the call gets a bad path, a missing file and a native library that will not load as a
    /// faulted task, in the same place as everything else, instead of as a throw from the call itself.
    /// </remarks>
    public static async Task<ModelDetails> ProbeAsync(
        string modelPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            throw new ArgumentException("A probe needs the path of a GGUF file.", nameof(modelPath));
        }

        if (!File.Exists(modelPath))
        {
            throw new ModelLoadException($"There is no model file at '{modelPath}'.");
        }

        NativeLibraryLoader.EnsureLoaded();
        EngineLog.Arm();

        // A probe is short and touches no context, so it runs on its own thread rather than on a worker:
        // there is nothing for a worker to keep affinity with.
        return await Task.Run(() => Probe(modelPath, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }

    private static ModelDetails Probe(string modelPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EngineLog.Clear();

        // no_alloc is the engine's own "only load metadata and simulate memory allocations", which reads
        // every number ModelDetails promises - the weights' size included - without reading a weight.
        bool vocabularyOnly = false;
        IntPtr model = Load(modelPath, false);

        // A file the engine will not open that way is still worth describing, so the vocabulary-only load is
        // tried next; it reads the tokenizer and the metadata but reports nothing about the tensors.
        if (model == IntPtr.Zero)
        {
            vocabularyOnly = true;
            model = Load(modelPath, true);
        }

        if (model == IntPtr.Zero)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new ModelLoadException(EngineLog.Describe(
                $"The engine could not read '{modelPath}' as a model."));
        }

        using (SafeLlamaModelHandle handle = new SafeLlamaModelHandle(model))
        {
            return ModelDetailsBuilder.Build(model, modelPath, vocabularyOnly);
        }
    }

    private static unsafe IntPtr Load(string modelPath, bool vocabOnly)
    {
        LlamaModelParams parameters = ParameterMapper.BuildProbeParams(vocabOnly);
        return NativeMethods.llama_model_load_from_file(modelPath, parameters);
    }
}
