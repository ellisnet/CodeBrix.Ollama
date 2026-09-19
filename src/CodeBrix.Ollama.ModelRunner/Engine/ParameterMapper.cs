using System;
using System.IO;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Turns <see cref="ModelRunnerOptions"/> into the two native parameter structures, and says no to an
/// option set the engine would only reject later and less clearly.
/// </summary>
/// <remarks>
/// <para>
/// Both structures start from the values the loaded library itself reports as its defaults - see
/// <see cref="NativeDefaults"/> - and only the fields the options speak for are overwritten. A
/// <see langword="null"/> on a nullable option means "leave the engine's own answer alone", which for
/// several fields is not the same as zero.
/// </para>
/// </remarks>
internal static unsafe class ParameterMapper
{
    /// <summary>Checks an option set before anything native is touched.</summary>
    /// <param name="options">The options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">An option is out of range, or two options contradict each other.</exception>
    /// <exception cref="ModelLoadException">The model file, or one of the adapter files, is not there.</exception>
    public static void Validate(ModelRunnerOptions options)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrWhiteSpace(options.ModelPath))
        {
            throw new ArgumentException(
                $"{nameof(ModelRunnerOptions)}.{nameof(ModelRunnerOptions.ModelPath)} must name the model's "
                + "GGUF file.", nameof(options));
        }

        if (options.BatchSize == 0)
        {
            throw new ArgumentException(
                $"{nameof(ModelRunnerOptions)}.{nameof(ModelRunnerOptions.BatchSize)} must be at least 1.",
                nameof(options));
        }

        if (options.PhysicalBatchSize == 0)
        {
            throw new ArgumentException(
                $"{nameof(ModelRunnerOptions)}.{nameof(ModelRunnerOptions.PhysicalBatchSize)} must be at "
                + "least 1.", nameof(options));
        }

        if (options.PhysicalBatchSize > options.BatchSize)
        {
            throw new ArgumentException(
                $"{nameof(ModelRunnerOptions)}.{nameof(ModelRunnerOptions.PhysicalBatchSize)} "
                + $"({options.PhysicalBatchSize}) cannot be larger than "
                + $"{nameof(ModelRunnerOptions.BatchSize)} ({options.BatchSize}): the physical batch is one "
                + "slice of the logical one.", nameof(options));
        }

        if (options.MaxSequences == 0)
        {
            throw new ArgumentException(
                $"{nameof(ModelRunnerOptions)}.{nameof(ModelRunnerOptions.MaxSequences)} must be at least 1.",
                nameof(options));
        }

        if (options.ContextSize.HasValue && options.ContextSize.Value == 0)
        {
            throw new ArgumentException(
                $"{nameof(ModelRunnerOptions)}.{nameof(ModelRunnerOptions.ContextSize)} must be at least 1; "
                + "leave it null to take the model's trained context.", nameof(options));
        }

        if (options.Threads.HasValue && options.Threads.Value < 1)
        {
            throw new ArgumentException(
                $"{nameof(ModelRunnerOptions)}.{nameof(ModelRunnerOptions.Threads)} must be at least 1; "
                + "leave it null to take the physical core count.", nameof(options));
        }

        if (options.BatchThreads.HasValue && options.BatchThreads.Value < 1)
        {
            throw new ArgumentException(
                $"{nameof(ModelRunnerOptions)}.{nameof(ModelRunnerOptions.BatchThreads)} must be at least 1; "
                + $"leave it null to take {nameof(ModelRunnerOptions.Threads)}.", nameof(options));
        }

        if (options.MaxThreads.HasValue && options.MaxThreads.Value < 1)
        {
            throw new ArgumentException(
                $"{nameof(ModelRunnerOptions)}.{nameof(ModelRunnerOptions.MaxThreads)} must be at least 1; "
                + "leave it null for no cap.", nameof(options));
        }

        if (!File.Exists(options.ModelPath))
        {
            throw new ModelLoadException($"There is no model file at '{options.ModelPath}'.");
        }

        foreach (LoraAdapterOptions adapter in options.LoraAdapters)
        {
            if (adapter == null || string.IsNullOrWhiteSpace(adapter.Path))
            {
                throw new ArgumentException(
                    $"Every {nameof(LoraAdapterOptions)} must name the adapter's GGUF file.", nameof(options));
            }

            if (!File.Exists(adapter.Path))
            {
                throw new ModelLoadException($"There is no LoRA adapter file at '{adapter.Path}'.");
            }
        }
    }

    /// <summary>Builds the model-loading parameters.</summary>
    /// <param name="options">The options.</param>
    /// <param name="progress">The progress state to wire in, or <see langword="null"/> for none.</param>
    /// <returns>The parameters.</returns>
    public static LlamaModelParams BuildModelParams(ModelRunnerOptions options, EngineLoadProgress progress)
    {
        LlamaModelParams parameters = NativeDefaults.ModelParams;

        // null leaves the library's own answer, which is -1 ("every layer on the accelerator") and becomes
        // "none" on a build with no accelerator.
        if (options.GpuLayers.HasValue) parameters.NGpuLayers = options.GpuLayers.Value;

        parameters.LoadMode = MapLoadMode(options.LoadMode);
        parameters.CheckTensors = options.CheckTensors ? (byte)1 : (byte)0;
        parameters.UseExtraBufts = options.UseExtraBufferTypes ? (byte)1 : (byte)0;
        parameters.LoadMtp = options.LoadMtpLayers ? (byte)1 : (byte)0;
        parameters.VocabOnly = 0;
        parameters.NoAlloc = 0;

        if (progress != null)
        {
            parameters.ProgressCallback = EngineLoadProgress.Callback();
            parameters.ProgressCallbackUserData = progress.Data();
        }

        return parameters;
    }

    /// <summary>Builds the parameters for a metadata-only probe: no weights are read and nothing is allocated.</summary>
    /// <param name="vocabOnly">Whether to fall back to reading the vocabulary rather than simulating the allocations.</param>
    /// <returns>The parameters.</returns>
    /// <remarks>
    /// The simulated load asks for no memory mapping. The engine maps a file straight into a backend buffer
    /// when it can, and that path has nothing to simulate: it asserts that no_alloc is off rather than
    /// returning an error. Without the mapping the loader takes the branch that hands every tensor a dummy
    /// buffer, which is exactly the simulation that is wanted, and no tensor data is read either way.
    /// </remarks>
    public static LlamaModelParams BuildProbeParams(bool vocabOnly)
    {
        LlamaModelParams parameters = NativeDefaults.ModelParams;
        parameters.NGpuLayers = 0;
        parameters.LoadMode = vocabOnly ? LlamaLoadMode.Mmap : LlamaLoadMode.None;
        parameters.CheckTensors = 0;
        parameters.VocabOnly = vocabOnly ? (byte)1 : (byte)0;
        parameters.NoAlloc = vocabOnly ? (byte)0 : (byte)1;
        return parameters;
    }

    /// <summary>Builds the inference-context parameters.</summary>
    /// <param name="options">The options.</param>
    /// <param name="abort">The abort flag to wire in, or <see langword="null"/> for none.</param>
    /// <param name="embeddings">Whether this context produces embeddings, overriding the option.</param>
    /// <param name="contextSize">The context size to ask for, or <see langword="null"/> to take the option's.</param>
    /// <returns>The parameters.</returns>
    public static LlamaContextParams BuildContextParams(
        ModelRunnerOptions options, EngineAbortFlag abort, bool embeddings, uint? contextSize)
    {
        LlamaContextParams parameters = NativeDefaults.ContextParams;

        uint? requested = contextSize ?? options.ContextSize;
        parameters.NCtx = requested ?? 0u;
        parameters.NBatch = options.BatchSize;
        parameters.NUbatch = options.PhysicalBatchSize;
        parameters.NSeqMax = options.MaxSequences;
        parameters.NRsSeq = options.RecurrentStateSnapshots;

        parameters.NThreads = ResolveThreads(options);
        parameters.NThreadsBatch = ResolveBatchThreads(options);

        parameters.FlashAttnType = MapFlashAttention(options.FlashAttention);
        parameters.TypeK = MapCacheType(options.KeyCacheType, parameters.TypeK);
        parameters.TypeV = MapCacheType(options.ValueCacheType, parameters.TypeV);

        if (options.RopeFrequencyBase.HasValue) parameters.RopeFreqBase = options.RopeFrequencyBase.Value;
        if (options.RopeFrequencyScale.HasValue) parameters.RopeFreqScale = options.RopeFrequencyScale.Value;

        parameters.Embeddings = embeddings ? (byte)1 : (byte)0;
        parameters.PoolingType = MapPooling(options.EmbeddingPooling);
        parameters.NoPerf = options.CollectTimings ? (byte)0 : (byte)1;

        if (abort != null)
        {
            parameters.AbortCallback = EngineAbortFlag.Callback();
            parameters.AbortCallbackData = abort.Data();
        }

        return parameters;
    }

    /// <summary>The thread count a context should generate with.</summary>
    /// <param name="options">The options.</param>
    /// <returns>The count, never below one.</returns>
    /// <remarks>
    /// <see cref="ModelRunnerOptions.Threads"/> is used exactly as it stands and
    /// <see cref="ModelRunnerOptions.MaxThreads"/> is ignored for it; a cap bounds only the physical-core
    /// count this falls back to. See <see cref="EngineThreadCount"/>, which is the one place the rule is
    /// written.
    /// </remarks>
    public static int ResolveThreads(ModelRunnerOptions options) =>
        ResolveThreads(options, EnginePhysicalCores.Count());

    /// <summary>
    /// The thread count a context should generate with, against a stated detected count, which is how the
    /// rule is exercised against a processor other than the one the code is running on.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="detected">What the processor suggests when the options state no count.</param>
    /// <returns>The count, never below one.</returns>
    public static int ResolveThreads(ModelRunnerOptions options, int detected) =>
        EngineThreadCount.Resolve(options.Threads, options.MaxThreads, detected);

    /// <summary>The thread count a context should process a prompt with.</summary>
    /// <param name="options">The options.</param>
    /// <returns>The count, never below one.</returns>
    /// <remarks>
    /// <see cref="ModelRunnerOptions.BatchThreads"/> is used exactly as it stands; with none stated this is
    /// whatever <see cref="ResolveThreads(ModelRunnerOptions)"/> answered, cap and all.
    /// </remarks>
    public static int ResolveBatchThreads(ModelRunnerOptions options) =>
        options.BatchThreads ?? ResolveThreads(options);

    /// <summary>Maps the public load mode onto the engine's.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>The engine's enumeration value.</returns>
    /// <exception cref="ArgumentException">The mode is not one of the named ones.</exception>
    public static LlamaLoadMode MapLoadMode(ModelLoadMode mode) =>
        mode switch
        {
            ModelLoadMode.Read => LlamaLoadMode.None,
            ModelLoadMode.MemoryMap => LlamaLoadMode.Mmap,
            ModelLoadMode.LockInMemory => LlamaLoadMode.Mlock,
            ModelLoadMode.MemoryMapAndLock => LlamaLoadMode.MmapMlock,
            ModelLoadMode.DirectIo => LlamaLoadMode.DirectIo,
            _ => throw new ArgumentException($"'{mode}' is not a load mode this library knows.", nameof(mode)),
        };

    /// <summary>Maps the public flash-attention setting onto the engine's.</summary>
    /// <param name="mode">The setting.</param>
    /// <returns>The engine's enumeration value.</returns>
    /// <exception cref="ArgumentException">The setting is not one of the named ones.</exception>
    public static LlamaFlashAttnType MapFlashAttention(FlashAttentionMode mode) =>
        mode switch
        {
            FlashAttentionMode.Auto => LlamaFlashAttnType.Auto,
            FlashAttentionMode.Disabled => LlamaFlashAttnType.Disabled,
            FlashAttentionMode.Enabled => LlamaFlashAttnType.Enabled,
            _ => throw new ArgumentException(
                $"'{mode}' is not a flash-attention setting this library knows.", nameof(mode)),
        };

    /// <summary>Maps the public pooling setting onto the engine's.</summary>
    /// <param name="pooling">The setting.</param>
    /// <returns>The engine's enumeration value.</returns>
    /// <exception cref="ArgumentException">The setting is not one of the named ones.</exception>
    public static LlamaPoolingType MapPooling(EmbeddingPooling pooling) =>
        pooling switch
        {
            EmbeddingPooling.Unspecified => LlamaPoolingType.Unspecified,
            EmbeddingPooling.None => LlamaPoolingType.None,
            EmbeddingPooling.Mean => LlamaPoolingType.Mean,
            EmbeddingPooling.Cls => LlamaPoolingType.Cls,
            EmbeddingPooling.Last => LlamaPoolingType.Last,
            EmbeddingPooling.Rank => LlamaPoolingType.Rank,
            _ => throw new ArgumentException(
                $"'{pooling}' is not a pooling setting this library knows.", nameof(pooling)),
        };

    /// <summary>Maps a key or value cache type onto a ggml tensor type.</summary>
    /// <param name="type">The setting.</param>
    /// <param name="engineDefault">What the engine chose, returned for <see cref="KvCacheType.Default"/>.</param>
    /// <returns>The tensor type.</returns>
    /// <exception cref="ArgumentException">The setting is not one of the named ones.</exception>
    public static GgmlType MapCacheType(KvCacheType type, GgmlType engineDefault) =>
        type switch
        {
            KvCacheType.Default => engineDefault,
            KvCacheType.F32 => GgmlType.F32,
            KvCacheType.F16 => GgmlType.F16,
            KvCacheType.BF16 => GgmlType.Bf16,
            KvCacheType.Q8_0 => GgmlType.Q8_0,
            KvCacheType.Q4_0 => GgmlType.Q4_0,
            _ => throw new ArgumentException(
                $"'{type}' is not a cache type this library knows.", nameof(type)),
        };
}
