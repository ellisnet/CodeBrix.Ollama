using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;

/// <summary>
/// The native entry points over a context's key/value memory: clearing it, and moving, copying, keeping and
/// shifting the tokens of one sequence.
/// </summary>
internal static unsafe partial class NativeMethods
{
    /// <summary>Clears the key/value memory; clearing the data as well is slower but releases it.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void llama_memory_clear(IntPtr mem, [MarshalAs(UnmanagedType.U1)] bool data);

    /// <summary>Drops a sequence's tokens in the half-open position range; a negative sequence matches any.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool llama_memory_seq_rm(IntPtr mem, int seqId, int p0, int p1);

    /// <summary>Copies one sequence's tokens on to another sequence.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void llama_memory_seq_cp(IntPtr mem, int seqIdSrc, int seqIdDst, int p0, int p1);

    /// <summary>Drops every token that does not belong to one sequence.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void llama_memory_seq_keep(IntPtr mem, int seqId);

    /// <summary>Shifts a sequence's positions in a range by a delta.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void llama_memory_seq_add(IntPtr mem, int seqId, int p0, int p1, int delta);

    /// <summary>Divides a sequence's positions in a range by a factor greater than one.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void llama_memory_seq_div(IntPtr mem, int seqId, int p0, int p1, int d);

    /// <summary>The smallest position a sequence still has in memory, or -1 when it is empty.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_memory_seq_pos_min(IntPtr mem, int seqId);

    /// <summary>The largest position a sequence has in memory, or -1 when it is empty.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_memory_seq_pos_max(IntPtr mem, int seqId);

    /// <summary>Whether the memory supports shifting positions.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool llama_memory_can_shift(IntPtr mem);
}
