using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;

/// <summary>
/// The native entry points that save and restore a context's state, whole or one sequence at a time.
/// </summary>
internal static unsafe partial class NativeMethods
{
    /// <summary>No extra behaviour: <c>LLAMA_STATE_SEQ_FLAGS_NONE</c>.</summary>
    internal const uint LlamaStateSeqFlagsNone = 0;

    /// <summary>Work only with partial states such as a sliding-window or recurrent cache.</summary>
    internal const uint LlamaStateSeqFlagsPartialOnly = 1;

    /// <summary>Keep the tensor data on device buffers: faster, but not readable from host memory.</summary>
    internal const uint LlamaStateSeqFlagsOnDevice = 2;

    /// <summary>The size in bytes a full state copy needs. Only ask when saving.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial nuint llama_state_get_size(IntPtr ctx);

    /// <summary>Copies the whole state out; returns the bytes written.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial nuint llama_state_get_data(IntPtr ctx, byte* dst, nuint size);

    /// <summary>Reads a whole state back in; returns the bytes read.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial nuint llama_state_set_data(IntPtr ctx, byte* src, nuint size);

    /// <summary>Loads a session file and its tokens.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool llama_state_load_file(IntPtr ctx, string pathSession, int* tokensOut, nuint nTokenCapacity, nuint* nTokenCountOut);

    /// <summary>Writes a session file and its tokens.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool llama_state_save_file(IntPtr ctx, string pathSession, int* tokens, nuint nTokenCount);

    /// <summary>The size in bytes one sequence's state needs.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial nuint llama_state_seq_get_size(IntPtr ctx, int seqId);

    /// <summary>Copies one sequence's state out; returns the bytes written.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial nuint llama_state_seq_get_data(IntPtr ctx, byte* dst, nuint size, int seqId);

    /// <summary>Reads one sequence's state in; positive means success, zero means it failed.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial nuint llama_state_seq_set_data(IntPtr ctx, byte* src, nuint size, int destSeqId);

    /// <summary>Writes one sequence's state and its tokens to a file.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial nuint llama_state_seq_save_file(IntPtr ctx, string filepath, int seqId, int* tokens, nuint nTokenCount);

    /// <summary>Reads one sequence's state and its tokens from a file.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial nuint llama_state_seq_load_file(IntPtr ctx, string filepath, int destSeqId, int* tokensOut, nuint nTokenCapacity, nuint* nTokenCountOut);

    /// <summary>The size one sequence's state needs, with the flag bits applied.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial nuint llama_state_seq_get_size_ext(IntPtr ctx, int seqId, uint flags);

    /// <summary>Copies one sequence's state out, with the flag bits applied.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial nuint llama_state_seq_get_data_ext(IntPtr ctx, byte* dst, nuint size, int seqId, uint flags);

    /// <summary>Reads one sequence's state in, with the flag bits applied.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial nuint llama_state_seq_set_data_ext(IntPtr ctx, byte* src, nuint size, int destSeqId, uint flags);
}
