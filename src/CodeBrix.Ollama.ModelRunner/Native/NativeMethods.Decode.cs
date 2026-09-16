using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;

/// <summary>
/// The native entry points that run a batch through the model and read the results back.
/// </summary>
internal static unsafe partial class NativeMethods
{
    /// <summary>Runs a batch through the encoder without touching the key/value memory; zero means success.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_encode(IntPtr ctx, LlamaBatch batch);

    /// <summary>Runs a batch through the decoder; zero means success, 1 means no free cache slot, negatives are errors.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_decode(IntPtr ctx, LlamaBatch batch);

    /// <summary>The logits of the last decode, one row of <c>n_vocab</c> per requested output, in batch order.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial float* llama_get_logits(IntPtr ctx);

    /// <summary>The logits of one output row; negative indices count back from the last. Null when the index is wrong.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial float* llama_get_logits_ith(IntPtr ctx, int i);

    /// <summary>Every output embedding of the last decode, or null.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial float* llama_get_embeddings(IntPtr ctx);

    /// <summary>One output row's embedding; negative indices count back from the last. Null when the index is wrong.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial float* llama_get_embeddings_ith(IntPtr ctx, int i);

    /// <summary>A whole sequence's pooled embedding, or null when pooling is off.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial float* llama_get_embeddings_seq(IntPtr ctx, int seqId);

    /// <summary>The token a backend sampler chose for one output row, or -1.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_get_sampled_token_ith(IntPtr ctx, int i);

    /// <summary>The probabilities a backend sampler produced for one output row, or null.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial float* llama_get_sampled_probs_ith(IntPtr ctx, int i);

    /// <summary>How many probabilities <c>llama_get_sampled_probs_ith</c> returned.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial uint llama_get_sampled_probs_count_ith(IntPtr ctx, int i);

    /// <summary>The logits a backend sampler produced for one output row, or null.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial float* llama_get_sampled_logits_ith(IntPtr ctx, int i);

    /// <summary>How many logits <c>llama_get_sampled_logits_ith</c> returned.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial uint llama_get_sampled_logits_count_ith(IntPtr ctx, int i);

    /// <summary>The candidate token ids behind a backend sampler's probabilities, or null.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int* llama_get_sampled_candidates_ith(IntPtr ctx, int i);

    /// <summary>How many candidates <c>llama_get_sampled_candidates_ith</c> returned.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial uint llama_get_sampled_candidates_count_ith(IntPtr ctx, int i);
}
