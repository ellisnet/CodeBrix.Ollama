using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;

/// <summary>
/// The native entry points for sampler chains and every sampler the engine ships.
/// </summary>
internal static unsafe partial class NativeMethods
{
    /// <summary>Builds a sampler from a caller-supplied virtual table.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr llama_sampler_init(LlamaSamplerInterface* iface, void* ctx);

    /// <summary>A sampler's name, owned by the sampler.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial byte* llama_sampler_name(IntPtr smpl);

    /// <summary>Tells a sampler which token was chosen.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void llama_sampler_accept(IntPtr smpl, int token);

    /// <summary>Runs a sampler over a candidate set.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void llama_sampler_apply(IntPtr smpl, LlamaTokenDataArray* curP);

    /// <summary>Returns a sampler to its initial state.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void llama_sampler_reset(IntPtr smpl);

    /// <summary>Copies a sampler.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr llama_sampler_clone(IntPtr smpl);

    /// <summary>Releases a sampler. Never free one that a chain has taken ownership of.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void llama_sampler_free(IntPtr smpl);

    /// <summary>Creates an empty sampler chain.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr llama_sampler_chain_init(LlamaSamplerChainParams chainParams);

    /// <summary>Appends a sampler to a chain, which takes ownership of it.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void llama_sampler_chain_add(IntPtr chain, IntPtr smpl);

    /// <summary>The sampler at an index, or the chain itself for -1; null when the chain or index is wrong.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr llama_sampler_chain_get(IntPtr chain, int i);

    /// <summary>How many samplers a chain holds.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_sampler_chain_n(IntPtr chain);

    /// <summary>Removes a sampler from a chain and gives its ownership back to the caller.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr llama_sampler_chain_remove(IntPtr chain, int i);

    /// <summary>Greedy selection of the highest logit.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr llama_sampler_init_greedy();

    /// <summary>Samples from the distribution; pass 0xFFFFFFFF for a random seed.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr llama_sampler_init_dist(uint seed);

    /// <summary>Top-k truncation; a value of zero or less does nothing.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr llama_sampler_init_top_k(int k);

    /// <summary>Nucleus (top-p) truncation.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr llama_sampler_init_top_p(float p, nuint minKeep);

    /// <summary>Minimum-probability truncation.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr llama_sampler_init_min_p(float p, nuint minKeep);

    /// <summary>Locally typical sampling.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr llama_sampler_init_typical(float p, nuint minKeep);

    /// <summary>Temperature scaling; zero or less keeps the highest logit and discards the rest.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr llama_sampler_init_temp(float t);

    /// <summary>Dynamic (entropy-driven) temperature.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr llama_sampler_init_temp_ext(float t, float delta, float exponent);

    /// <summary>Exclude-top-choices sampling.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr llama_sampler_init_xtc(float p, float t, nuint minKeep, uint seed);

    /// <summary>Top-n-sigma truncation.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr llama_sampler_init_top_n_sigma(float n);

    /// <summary>Mirostat 1.0.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr llama_sampler_init_mirostat(int nVocab, uint seed, float tau, float eta, int m);

    /// <summary>Mirostat 2.0.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr llama_sampler_init_mirostat_v2(uint seed, float tau, float eta);

    /// <summary>A GBNF grammar constraint; null when the grammar does not parse.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr llama_sampler_init_grammar(IntPtr vocab, string grammarStr, string grammarRoot);

    /// <summary>A grammar constraint that only engages once a pattern or token has been generated.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr llama_sampler_init_grammar_lazy_patterns(IntPtr vocab, byte* grammarStr, byte* grammarRoot, byte** triggerPatterns, nuint numTriggerPatterns, int* triggerTokens, nuint numTriggerTokens);

    /// <summary>Repetition, frequency and presence penalties.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr llama_sampler_init_penalties(int penaltyLastN, float penaltyRepeat, float penaltyFreq, float penaltyPresent);

    /// <summary>The DRY repetition sampler.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr llama_sampler_init_dry(IntPtr vocab, int nCtxTrain, float dryMultiplier, float dryBase, int dryAllowedLength, int dryPenaltyLastN, byte** seqBreakers, nuint numBreakers);

    /// <summary>Adaptive-p: selects tokens near a target probability. Must come last in a chain.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr llama_sampler_init_adaptive_p(float target, float decay, uint seed);

    /// <summary>Adds a fixed bias to named tokens' logits.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr llama_sampler_init_logit_bias(int nVocab, int nLogitBias, LlamaLogitBias* logitBias);

    /// <summary>The fill-in-the-middle infill sampler.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr llama_sampler_init_infill(IntPtr vocab);

    /// <summary>The seed a sampler uses, or 0xFFFFFFFF when it has none.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial uint llama_sampler_get_seed(IntPtr smpl);

    /// <summary>Samples and accepts a token from one output row of the last decode.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_sampler_sample(IntPtr smpl, IntPtr ctx, int idx);
}
