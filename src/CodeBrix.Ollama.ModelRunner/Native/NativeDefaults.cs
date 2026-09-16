namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The parameter structures the native library itself considers the defaults.
/// </summary>
/// <remarks>
/// Always start from one of these rather than from a zeroed structure. Several of the engine's defaults are
/// not zero - <c>n_gpu_layers</c> is -1, <c>load_mode</c> is memory-mapped, the YaRN factors are -1 and
/// <c>flash_attn_type</c> is "decide for me" - and a zeroed structure asks for something quite different.
/// Reading them from the library rather than repeating them here also means the values can never drift from
/// the build that is actually loaded.
/// </remarks>
internal static class NativeDefaults
{
    /// <summary>The model-loading defaults, straight from <c>llama_model_default_params</c>.</summary>
    public static LlamaModelParams ModelParams
    {
        get
        {
            NativeLibraryLoader.EnsureLoaded();
            return NativeMethods.llama_model_default_params();
        }
    }

    /// <summary>The context defaults, straight from <c>llama_context_default_params</c>.</summary>
    public static LlamaContextParams ContextParams
    {
        get
        {
            NativeLibraryLoader.EnsureLoaded();
            return NativeMethods.llama_context_default_params();
        }
    }

    /// <summary>The sampler-chain defaults, straight from <c>llama_sampler_chain_default_params</c>.</summary>
    public static LlamaSamplerChainParams SamplerChainParams
    {
        get
        {
            NativeLibraryLoader.EnsureLoaded();
            return NativeMethods.llama_sampler_chain_default_params();
        }
    }

    /// <summary>The quantization defaults, straight from <c>llama_model_quantize_default_params</c>.</summary>
    public static LlamaModelQuantizeParams QuantizeParams
    {
        get
        {
            NativeLibraryLoader.EnsureLoaded();
            return NativeMethods.llama_model_quantize_default_params();
        }
    }
}
