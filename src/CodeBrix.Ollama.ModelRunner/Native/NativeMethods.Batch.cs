using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;

/// <summary>
/// The native entry points that allocate and release a decode batch.
/// </summary>
internal static unsafe partial class NativeMethods
{
    /// <summary>Wraps a token array as a single-sequence batch. A transitional helper; prefer building a batch.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial LlamaBatch llama_batch_get_one(int* tokens, int nTokens);

    /// <summary>Allocates a batch on the heap. Every member is left uninitialized.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial LlamaBatch llama_batch_init(int nTokens, int embd, int nSeqMax);

    /// <summary>Releases a batch allocated by <c>llama_batch_init</c>.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void llama_batch_free(LlamaBatch batch);
}
