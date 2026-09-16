using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;

/// <summary>
/// The native entry points that create an inference context, read back what it settled on, and set the
/// knobs that can be changed after it exists.
/// </summary>
internal static unsafe partial class NativeMethods
{
    /// <summary>Creates an inference context over a loaded model; returns zero on failure.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr llama_init_from_model(IntPtr model, LlamaContextParams contextParams);

    /// <summary>Releases a context and everything it allocated.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void llama_free(IntPtr ctx);

    /// <summary>The context size the context actually settled on.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial uint llama_n_ctx(IntPtr ctx);

    /// <summary>The per-sequence context size.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial uint llama_n_ctx_seq(IntPtr ctx);

    /// <summary>The logical batch size the context settled on.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial uint llama_n_batch(IntPtr ctx);

    /// <summary>The physical micro-batch size the context settled on.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial uint llama_n_ubatch(IntPtr ctx);

    /// <summary>How many sequences the context can hold.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial uint llama_n_seq_max(IntPtr ctx);

    /// <summary>How many recurrent-state snapshots the context keeps per sequence.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial uint llama_n_rs_seq(IntPtr ctx);

    /// <summary>The model the context was created over, owned by the caller of the load.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr llama_get_model(IntPtr ctx);

    /// <summary>The context's key/value memory, owned by the context.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr llama_get_memory(IntPtr ctx);

    /// <summary>The pooling the context applies to embeddings.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial LlamaPoolingType llama_pooling_type(IntPtr ctx);

    /// <summary>Sets the thread counts for single-token and batch work.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void llama_set_n_threads(IntPtr ctx, int nThreads, int nThreadsBatch);

    /// <summary>The threads used to generate one token.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_n_threads(IntPtr ctx);

    /// <summary>The threads used to process a batch.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_n_threads_batch(IntPtr ctx);

    /// <summary>Turns embedding output on or off.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void llama_set_embeddings(IntPtr ctx, [MarshalAs(UnmanagedType.U1)] bool embeddings);

    /// <summary>Turns causal masking on or off.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void llama_set_causal_attn(IntPtr ctx, [MarshalAs(UnmanagedType.U1)] bool causalAttn);

    /// <summary>Installs the callback that can abort a decode; returning non-zero from it aborts.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void llama_set_abort_callback(IntPtr ctx, delegate* unmanaged[Cdecl]<void*, byte> abortCallback, void* abortCallbackData);

    /// <summary>Waits for every pending computation to finish.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void llama_synchronize(IntPtr ctx);

    /// <summary>Attaches a backend sampler chain to one sequence of a context.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool llama_set_sampler(IntPtr ctx, int seqId, IntPtr smpl);

    /// <summary>The tensor filter that accepts every tensor, for training.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool llama_opt_param_filter_all(IntPtr tensor, void* userdata);

    /// <summary>Prepares a context for training.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void llama_opt_init(IntPtr lctx, IntPtr model, LlamaOptParams loptParams);

    /// <summary>Runs one training epoch over a dataset.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void llama_opt_epoch(IntPtr lctx, IntPtr dataset, IntPtr resultTrain, IntPtr resultEval, long idataSplit, delegate* unmanaged[Cdecl]<byte, IntPtr, IntPtr, IntPtr, long, long, long, void> callbackTrain, delegate* unmanaged[Cdecl]<byte, IntPtr, IntPtr, IntPtr, long, long, long, void> callbackEval);
}
