using System;
using System.IO;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Covers the translation from <see cref="ModelRunnerOptions"/> to the two native parameter structures, and
/// the option combinations the library refuses before it touches the engine at all.
/// </summary>
/// <remarks>
/// The structures start from the loaded library's own defaults, so the tests compare against those rather
/// than against constants written down here: a default that changed upstream would otherwise be baked into
/// the test suite instead of being noticed.
/// </remarks>
public sealed unsafe class ParameterMapperTests
{
    private static ModelRunnerOptions Minimal() =>
        new ModelRunnerOptions { ModelPath = TestVectors.ConformanceModelPath };

    /// <summary>Options with nothing but a path pass.</summary>
    [Fact]
    public void Validate_accepts_an_option_set_with_only_a_model_path()
    {
        ParameterMapper.Validate(Minimal());
    }

    /// <summary>A null option set is a programming error.</summary>
    [Fact]
    public void Validate_refuses_a_null_option_set()
    {
        Assert.Throws<ArgumentNullException>(() => ParameterMapper.Validate(null));
    }

    /// <summary>A missing model path is an argument problem, not a load problem.</summary>
    [Fact]
    public void Validate_refuses_an_option_set_without_a_model_path()
    {
        Assert.Throws<ArgumentException>(() => ParameterMapper.Validate(new ModelRunnerOptions()));
    }

    /// <summary>A path that names nothing is a load problem, and the message says which path.</summary>
    [Fact]
    public void Validate_refuses_a_model_path_that_names_nothing()
    {
        //Arrange
        ModelRunnerOptions options = new ModelRunnerOptions
        {
            ModelPath = Path.Combine(AppContext.BaseDirectory, "no-such-model.gguf"),
        };

        //Act
        ModelLoadException error = Assert.Throws<ModelLoadException>(() => ParameterMapper.Validate(options));

        //Assert
        error.Message.Should().Contain("no-such-model.gguf");
    }

    /// <summary>A physical batch larger than the logical one cannot be what the caller meant.</summary>
    [Fact]
    public void Validate_refuses_a_physical_batch_larger_than_the_logical_batch()
    {
        //Arrange
        ModelRunnerOptions options = Minimal();
        options.BatchSize = 256;
        options.PhysicalBatchSize = 512;

        //Act and assert
        Assert.Throws<ArgumentException>(() => ParameterMapper.Validate(options));
    }

    /// <summary>Zeroes where the engine needs at least one are refused.</summary>
    [Theory]
    [InlineData("batch")]
    [InlineData("ubatch")]
    [InlineData("sequences")]
    [InlineData("context")]
    [InlineData("threads")]
    [InlineData("batchThreads")]
    public void Validate_refuses_a_count_the_engine_cannot_use(string which)
    {
        //Arrange
        ModelRunnerOptions options = Minimal();
        switch (which)
        {
            case "batch": options.BatchSize = 0; break;
            case "ubatch": options.PhysicalBatchSize = 0; break;
            case "sequences": options.MaxSequences = 0; break;
            case "context": options.ContextSize = 0; break;
            case "threads": options.Threads = 0; break;
            case "batchThreads": options.BatchThreads = 0; break;
        }

        //Act and assert
        Assert.Throws<ArgumentException>(() => ParameterMapper.Validate(options));
    }

    /// <summary>An adapter whose file is not there is a load problem.</summary>
    [Fact]
    public void Validate_refuses_a_lora_adapter_whose_file_is_missing()
    {
        //Arrange
        ModelRunnerOptions options = Minimal();
        options.LoraAdapters.Add(new LoraAdapterOptions { Path = Path.Combine(AppContext.BaseDirectory, "nope.gguf") });

        //Act and assert
        Assert.Throws<ModelLoadException>(() => ParameterMapper.Validate(options));
    }

    /// <summary>Every model-loading option reaches the field the engine reads it from.</summary>
    [Fact]
    public void BuildModelParams_maps_every_model_option()
    {
        //Arrange
        NativeLibraryLoader.EnsureLoaded();
        ModelRunnerOptions options = Minimal();
        options.GpuLayers = 7;
        options.LoadMode = ModelLoadMode.LockInMemory;
        options.CheckTensors = true;
        options.UseExtraBufferTypes = false;
        options.LoadMtpLayers = true;

        //Act
        LlamaModelParams mapped = ParameterMapper.BuildModelParams(options, null);

        //Assert
        mapped.NGpuLayers.Should().Be(7);
        mapped.LoadMode.Should().Be(LlamaLoadMode.Mlock);
        mapped.CheckTensors.Should().Be((byte)1);
        mapped.UseExtraBufts.Should().Be((byte)0);
        mapped.LoadMtp.Should().Be((byte)1);
        mapped.VocabOnly.Should().Be((byte)0);
        mapped.NoAlloc.Should().Be((byte)0);
        ((IntPtr)mapped.ProgressCallback).Should().Be(IntPtr.Zero);
    }

    /// <summary>A null layer count leaves the engine's own answer, which is not zero.</summary>
    [Fact]
    public void BuildModelParams_leaves_gpu_layers_alone_when_the_option_is_null()
    {
        //Arrange
        NativeLibraryLoader.EnsureLoaded();
        LlamaModelParams defaults = NativeDefaults.ModelParams;

        //Act
        LlamaModelParams mapped = ParameterMapper.BuildModelParams(Minimal(), null);

        //Assert
        mapped.NGpuLayers.Should().Be(defaults.NGpuLayers);
    }

    /// <summary>A progress sink is wired to the engine's callback, and only then.</summary>
    [Fact]
    public void BuildModelParams_wires_the_progress_callback_when_there_is_a_sink()
    {
        //Arrange
        NativeLibraryLoader.EnsureLoaded();

        //Act
        using EngineLoadProgress progress = new EngineLoadProgress(new Progress<float>(_ => { }), default);
        LlamaModelParams mapped = ParameterMapper.BuildModelParams(Minimal(), progress);

        //Assert
        ((IntPtr)mapped.ProgressCallback).Should().NotBe(IntPtr.Zero);
        ((IntPtr)mapped.ProgressCallbackUserData).Should().NotBe(IntPtr.Zero);
    }

    /// <summary>A probe asks for metadata only and simulates the allocations rather than making them.</summary>
    [Fact]
    public void BuildProbeParams_asks_for_metadata_only()
    {
        //Arrange
        NativeLibraryLoader.EnsureLoaded();

        //Act
        LlamaModelParams simulated = ParameterMapper.BuildProbeParams(false);
        LlamaModelParams vocabulary = ParameterMapper.BuildProbeParams(true);

        //Assert
        simulated.NoAlloc.Should().Be((byte)1);
        simulated.VocabOnly.Should().Be((byte)0);
        simulated.NGpuLayers.Should().Be(0);

        // The engine's mmap-into-a-backend-buffer path asserts that no_alloc is off, so a simulated load
        // must not ask for a mapping.
        simulated.LoadMode.Should().Be(LlamaLoadMode.None);
        vocabulary.NoAlloc.Should().Be((byte)0);
        vocabulary.VocabOnly.Should().Be((byte)1);
    }

    /// <summary>Every context option reaches the field the engine reads it from.</summary>
    [Fact]
    public void BuildContextParams_maps_every_context_option()
    {
        //Arrange
        NativeLibraryLoader.EnsureLoaded();
        ModelRunnerOptions options = Minimal();
        options.ContextSize = 4096;
        options.BatchSize = 1024;
        options.PhysicalBatchSize = 256;
        options.MaxSequences = 3;
        options.RecurrentStateSnapshots = 2;
        options.Threads = 5;
        options.BatchThreads = 9;
        options.FlashAttention = FlashAttentionMode.Disabled;
        options.KeyCacheType = KvCacheType.Q8_0;
        options.ValueCacheType = KvCacheType.F32;
        options.RopeFrequencyBase = 500000f;
        options.RopeFrequencyScale = 0.25f;
        options.EmbeddingPooling = EmbeddingPooling.Mean;
        options.CollectTimings = false;

        //Act
        LlamaContextParams mapped = ParameterMapper.BuildContextParams(options, null, false, null);

        //Assert
        mapped.NCtx.Should().Be(4096u);
        mapped.NBatch.Should().Be(1024u);
        mapped.NUbatch.Should().Be(256u);
        mapped.NSeqMax.Should().Be(3u);
        mapped.NRsSeq.Should().Be(2u);
        mapped.NThreads.Should().Be(5);
        mapped.NThreadsBatch.Should().Be(9);
        mapped.FlashAttnType.Should().Be(LlamaFlashAttnType.Disabled);
        mapped.TypeK.Should().Be(GgmlType.Q8_0);
        mapped.TypeV.Should().Be(GgmlType.F32);
        mapped.RopeFreqBase.Should().Be(500000f);
        mapped.RopeFreqScale.Should().Be(0.25f);
        mapped.PoolingType.Should().Be(LlamaPoolingType.Mean);
        mapped.Embeddings.Should().Be((byte)0);
        mapped.NoPerf.Should().Be((byte)1);
    }

    /// <summary>A null context size asks the engine for the model's own trained context, which it spells zero.</summary>
    [Fact]
    public void BuildContextParams_asks_for_the_models_own_context_when_the_option_is_null()
    {
        //Arrange
        NativeLibraryLoader.EnsureLoaded();

        //Act
        LlamaContextParams mapped = ParameterMapper.BuildContextParams(Minimal(), null, false, null);

        //Assert
        mapped.NCtx.Should().Be(0u);
    }

    /// <summary>An explicit context size overrides the option, which is how the embeddings context is made.</summary>
    [Fact]
    public void BuildContextParams_lets_the_caller_override_the_context_size_and_the_embeddings_flag()
    {
        //Arrange
        NativeLibraryLoader.EnsureLoaded();
        ModelRunnerOptions options = Minimal();
        options.ContextSize = 8192;

        //Act
        LlamaContextParams mapped = ParameterMapper.BuildContextParams(options, null, true, 2048u);

        //Assert
        mapped.NCtx.Should().Be(2048u);
        mapped.Embeddings.Should().Be((byte)1);
    }

    /// <summary>Timings on means the engine's own counters are left running.</summary>
    [Fact]
    public void BuildContextParams_leaves_the_engines_timers_running_when_timings_are_collected()
    {
        //Arrange
        NativeLibraryLoader.EnsureLoaded();

        //Act
        LlamaContextParams mapped = ParameterMapper.BuildContextParams(Minimal(), null, false, null);

        //Assert
        mapped.NoPerf.Should().Be((byte)0);
    }

    /// <summary>A cancellable context gets the abort callback wired in.</summary>
    [Fact]
    public void BuildContextParams_wires_the_abort_callback()
    {
        //Arrange
        NativeLibraryLoader.EnsureLoaded();

        //Act
        using EngineAbortFlag abort = new EngineAbortFlag();
        LlamaContextParams mapped = ParameterMapper.BuildContextParams(Minimal(), abort, false, null);

        //Assert
        ((IntPtr)mapped.AbortCallback).Should().NotBe(IntPtr.Zero);
        ((IntPtr)mapped.AbortCallbackData).Should().NotBe(IntPtr.Zero);
    }

    /// <summary>With no thread count asked for, the physical core count is used.</summary>
    [Fact]
    public void ResolveThreads_falls_back_to_the_physical_core_count()
    {
        //Arrange
        ModelRunnerOptions options = Minimal();

        //Act
        int threads = ParameterMapper.ResolveThreads(options);

        //Assert
        threads.Should().Be(EnginePhysicalCores.Count());
        threads.Should().BeGreaterThan(0);
        (threads <= Environment.ProcessorCount).Should().BeTrue();
    }

    /// <summary>A thread count that was asked for is used as it stands.</summary>
    [Fact]
    public void ResolveThreads_uses_the_count_the_options_name()
    {
        //Arrange
        ModelRunnerOptions options = Minimal();
        options.Threads = 3;

        //Act and assert
        ParameterMapper.ResolveThreads(options).Should().Be(3);
    }

    /// <summary>Batch threads follow the generation threads unless they are set.</summary>
    [Fact]
    public void BuildContextParams_defaults_batch_threads_to_the_generation_threads()
    {
        //Arrange
        NativeLibraryLoader.EnsureLoaded();
        ModelRunnerOptions options = Minimal();
        options.Threads = 4;

        //Act
        LlamaContextParams mapped = ParameterMapper.BuildContextParams(options, null, false, null);

        //Assert
        mapped.NThreadsBatch.Should().Be(4);
    }

    /// <summary>
    /// Every load mode maps onto the engine's own. The engine's enumerations are internal to the library, so
    /// the expectation travels as the number the header gives it - which is also what actually crosses the
    /// boundary.
    /// </summary>
    [Theory]
    [InlineData(ModelLoadMode.Read, 0)]
    [InlineData(ModelLoadMode.MemoryMap, 1)]
    [InlineData(ModelLoadMode.LockInMemory, 2)]
    [InlineData(ModelLoadMode.MemoryMapAndLock, 3)]
    [InlineData(ModelLoadMode.DirectIo, 4)]
    public void MapLoadMode_maps_every_named_mode(ModelLoadMode mode, int expected)
    {
        ((int)ParameterMapper.MapLoadMode(mode)).Should().Be(expected);
    }

    /// <summary>Every flash-attention setting maps onto the engine's own.</summary>
    [Theory]
    [InlineData(FlashAttentionMode.Auto, -1)]
    [InlineData(FlashAttentionMode.Disabled, 0)]
    [InlineData(FlashAttentionMode.Enabled, 1)]
    public void MapFlashAttention_maps_every_named_setting(FlashAttentionMode mode, int expected)
    {
        ((int)ParameterMapper.MapFlashAttention(mode)).Should().Be(expected);
    }

    /// <summary>Every pooling setting maps onto the engine's own.</summary>
    [Theory]
    [InlineData(EmbeddingPooling.Unspecified, -1)]
    [InlineData(EmbeddingPooling.None, 0)]
    [InlineData(EmbeddingPooling.Mean, 1)]
    [InlineData(EmbeddingPooling.Cls, 2)]
    [InlineData(EmbeddingPooling.Last, 3)]
    [InlineData(EmbeddingPooling.Rank, 4)]
    public void MapPooling_maps_every_named_setting(EmbeddingPooling pooling, int expected)
    {
        ((int)ParameterMapper.MapPooling(pooling)).Should().Be(expected);
    }

    /// <summary>Every cache type maps onto a tensor type, and the default keeps whatever the engine chose.</summary>
    [Theory]
    [InlineData(KvCacheType.F32, 0)]
    [InlineData(KvCacheType.F16, 1)]
    [InlineData(KvCacheType.BF16, 30)]
    [InlineData(KvCacheType.Q8_0, 8)]
    [InlineData(KvCacheType.Q4_0, 2)]
    public void MapCacheType_maps_every_named_type(KvCacheType type, int expected)
    {
        ((int)ParameterMapper.MapCacheType(type, GgmlType.Iq4Nl)).Should().Be(expected);
    }

    /// <summary>The default cache type is whatever the engine already decided.</summary>
    [Fact]
    public void MapCacheType_keeps_the_engines_own_choice_for_the_default()
    {
        ParameterMapper.MapCacheType(KvCacheType.Default, GgmlType.Q5K).Should().Be(GgmlType.Q5K);
    }

    /// <summary>A value that is not one of the named ones is a programming error, not a silent fallback.</summary>
    [Fact]
    public void The_mappers_refuse_a_value_that_is_not_one_of_the_named_ones()
    {
        Assert.Throws<ArgumentException>(() => ParameterMapper.MapLoadMode((ModelLoadMode)99));
        Assert.Throws<ArgumentException>(() => ParameterMapper.MapFlashAttention((FlashAttentionMode)99));
        Assert.Throws<ArgumentException>(() => ParameterMapper.MapPooling((EmbeddingPooling)99));
        Assert.Throws<ArgumentException>(() => ParameterMapper.MapCacheType((KvCacheType)99, GgmlType.F16));
    }
}
