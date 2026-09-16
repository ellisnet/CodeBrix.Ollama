using System;
using System.Runtime.InteropServices;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Covers the parameter structures the library hands back as its defaults.
/// </summary>
/// <remarks>
/// This is the layout proof. The engine writes these structures field by field in C; if this binding's
/// declarations were the wrong size, in the wrong order or had the wrong types, the values read back here
/// would be garbage rather than the header's documented defaults. Checking the whole of both structures -
/// including the fields at the very end, which is where a missing field would show - is what makes the
/// check worth something.
/// </remarks>
public sealed class NativeDefaultsTests
{
    /// <summary>The structures are the sizes the C ABI gives them on a 64-bit platform.</summary>
    [Fact]
    public unsafe void Structure_sizes_match_the_c_abi()
    {
        //Act and assert
        sizeof(LlamaModelParams).Should().Be(72);
        sizeof(LlamaContextParams).Should().Be(160);
        sizeof(LlamaBatch).Should().Be(56);
        sizeof(LlamaTokenData).Should().Be(12);
        sizeof(LlamaTokenDataArray).Should().Be(32);
        sizeof(LlamaModelKvOverride).Should().Be(264);
        sizeof(LlamaModelQuantizeParams).Should().Be(56);
        sizeof(LlamaPerfContextData).Should().Be(48);
        sizeof(LlamaSamplerSeqConfig).Should().Be(16);
        sizeof(GgmlBackendDevProps).Should().Be(56);
        sizeof(LlamaSamplerInterface).Should().Be(80);
        sizeof(LlamaOptParams).Should().Be(48);
        sizeof(GgufInitParams).Should().Be(16);
        sizeof(LlamaPerfSamplerData).Should().Be(16);
    }

    /// <summary>The model defaults are the ones llama_model_default_params writes.</summary>
    [Fact]
    public unsafe void ModelParams_are_the_headers_defaults()
    {
        //Act
        LlamaModelParams defaults = NativeDefaults.ModelParams;

        //Assert
        ((IntPtr)defaults.Devices).Should().Be(IntPtr.Zero);
        ((IntPtr)defaults.TensorBuftOverrides).Should().Be(IntPtr.Zero);
        defaults.NGpuLayers.Should().Be(-1);
        defaults.SplitMode.Should().Be(LlamaSplitMode.Layer);
        defaults.LoadMode.Should().Be(LlamaLoadMode.Mmap);
        defaults.MainGpu.Should().Be(0);
        ((IntPtr)defaults.TensorSplit).Should().Be(IntPtr.Zero);
        ((IntPtr)defaults.ProgressCallback).Should().Be(IntPtr.Zero);
        ((IntPtr)defaults.ProgressCallbackUserData).Should().Be(IntPtr.Zero);
        ((IntPtr)defaults.KvOverrides).Should().Be(IntPtr.Zero);
        defaults.VocabOnly.Should().Be((byte)0);
        defaults.CheckTensors.Should().Be((byte)0);
        defaults.UseExtraBufts.Should().Be((byte)1);
        defaults.NoHost.Should().Be((byte)0);
        defaults.NoAlloc.Should().Be((byte)0);
        defaults.LoadMtp.Should().Be((byte)0);
    }

    /// <summary>The context defaults are the ones llama_context_default_params writes.</summary>
    [Fact]
    public unsafe void ContextParams_are_the_headers_defaults()
    {
        //Act
        LlamaContextParams defaults = NativeDefaults.ContextParams;

        //Assert
        defaults.NCtx.Should().Be(512u);
        defaults.NBatch.Should().Be(2048u);
        defaults.NUbatch.Should().Be(512u);
        defaults.NSeqMax.Should().Be(1u);
        defaults.NRsSeq.Should().Be(0u);
        defaults.NOutputsMax.Should().Be(0u);
        defaults.NThreads.Should().Be(4);
        defaults.NThreadsBatch.Should().Be(4);
        defaults.CtxType.Should().Be(LlamaContextType.Default);
        defaults.RopeScalingType.Should().Be(LlamaRopeScalingType.Unspecified);
        defaults.PoolingType.Should().Be(LlamaPoolingType.Unspecified);
        defaults.AttentionType.Should().Be(LlamaAttentionType.Unspecified);
        defaults.FlashAttnType.Should().Be(LlamaFlashAttnType.Auto);
        defaults.RopeFreqBase.Should().Be(0f);
        defaults.RopeFreqScale.Should().Be(0f);
        defaults.YarnExtFactor.Should().Be(-1f);
        defaults.YarnAttnFactor.Should().Be(-1f);
        defaults.YarnBetaFast.Should().Be(-1f);
        defaults.YarnBetaSlow.Should().Be(-1f);
        defaults.YarnOrigCtx.Should().Be(0u);
        defaults.DefragThold.Should().Be(-1f);
        ((IntPtr)defaults.CbEval).Should().Be(IntPtr.Zero);
        ((IntPtr)defaults.CbEvalUserData).Should().Be(IntPtr.Zero);
        defaults.TypeK.Should().Be(GgmlType.F16);
        defaults.TypeV.Should().Be(GgmlType.F16);
        ((IntPtr)defaults.AbortCallback).Should().Be(IntPtr.Zero);
        ((IntPtr)defaults.AbortCallbackData).Should().Be(IntPtr.Zero);
        defaults.Embeddings.Should().Be((byte)0);
        defaults.OffloadKqv.Should().Be((byte)1);
        defaults.NoPerf.Should().Be((byte)1);
        defaults.OpOffload.Should().Be((byte)1);
        defaults.SwaFull.Should().Be((byte)1);
        defaults.KvUnified.Should().Be((byte)0);
        ((IntPtr)defaults.Samplers).Should().Be(IntPtr.Zero);
        defaults.NSamplers.Should().Be((nuint)0);
        ((IntPtr)defaults.CtxOther).Should().Be(IntPtr.Zero);
    }

    /// <summary>The sampler chain defaults have the timers off, as the engine writes them.</summary>
    [Fact]
    public void SamplerChainParams_are_the_headers_defaults()
        => NativeDefaults.SamplerChainParams.NoPerf.Should().Be((byte)1);

    /// <summary>The quantization defaults come back with a sane thread count and file type.</summary>
    [Fact]
    public unsafe void QuantizeParams_are_the_headers_defaults()
    {
        //Act
        LlamaModelQuantizeParams defaults = NativeDefaults.QuantizeParams;

        //Assert
        defaults.NThread.Should().Be(0);
        defaults.OutputTensorType.Should().Be(GgmlType.Count);
        defaults.TokenEmbeddingType.Should().Be(GgmlType.Count);
        ((IntPtr)defaults.Imatrix).Should().Be(IntPtr.Zero);
        ((IntPtr)defaults.KvOverrides).Should().Be(IntPtr.Zero);
        ((IntPtr)defaults.TtOverrides).Should().Be(IntPtr.Zero);
        ((IntPtr)defaults.PruneLayers).Should().Be(IntPtr.Zero);
    }

    /// <summary>The capability queries answer without the engine having been asked to load anything.</summary>
    [Fact]
    public void Capability_queries_answer_on_this_platform()
    {
        //Arrange
        NativeLibraryLoader.EnsureLoaded();

        //Act
        bool mmap = NativeMethods.llama_supports_mmap();
        nuint maxDevices = NativeMethods.llama_max_devices();
        nuint maxSequences = NativeMethods.llama_max_parallel_sequences();

        //Assert
        mmap.Should().BeTrue();
        (maxDevices > 0).Should().BeTrue();
        (maxSequences > 0).Should().BeTrue();
    }
}
