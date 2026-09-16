using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;

/// <summary>
/// The native entry points for LoRA adapters and control vectors.
/// </summary>
internal static unsafe partial class NativeMethods
{
    /// <summary>Loads a LoRA adapter against a model; the adapter lives no longer than the model.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr llama_adapter_lora_init(IntPtr model, string pathLora);

    /// <summary>Copies one of an adapter's metadata values as text; returns its length, or -1.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_adapter_meta_val_str(IntPtr adapter, string key, byte* buf, nuint bufSize);

    /// <summary>How many metadata key/value pairs an adapter carries.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_adapter_meta_count(IntPtr adapter);

    /// <summary>Copies an adapter's metadata key at an index; returns its length, or -1.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_adapter_meta_key_by_index(IntPtr adapter, int i, byte* buf, nuint bufSize);

    /// <summary>Copies an adapter's metadata value at an index as text; returns its length, or -1.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_adapter_meta_val_str_by_index(IntPtr adapter, int i, byte* buf, nuint bufSize);

    /// <summary>Releases an adapter early. One that is not released here is released with its model.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void llama_adapter_lora_free(IntPtr adapter);

    /// <summary>How many invocation tokens an activated LoRA adapter has.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial ulong llama_adapter_get_alora_n_invocation_tokens(IntPtr adapter);

    /// <summary>The invocation tokens of an activated LoRA adapter, owned by the adapter.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int* llama_adapter_get_alora_invocation_tokens(IntPtr adapter);

    /// <summary>Sets the adapters, and their scales, that a context applies.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_set_adapters_lora(IntPtr ctx, IntPtr* adapters, nuint nAdapters, float* scales);

    /// <summary>Applies a control vector to a context, or clears it when the data pointer is null.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_set_adapter_cvec(IntPtr ctx, float* data, nuint len, int nEmbd, int ilStart, int ilEnd);
}
