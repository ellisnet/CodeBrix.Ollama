using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp ggml/include/ggml.h;

/// <summary>
/// The native entry points that redirect the engine's log output. The logger state is global and these are
/// not thread-safe, which is why <see cref="NativeLog"/> is the only caller.
/// </summary>
internal static unsafe partial class NativeMethods
{
    /// <summary>Routes every later log line to a callback. Null sends them to standard error.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void llama_log_set(delegate* unmanaged[Cdecl]<GgmlLogLevel, byte*, void*, void> logCallback, void* userData);

    /// <summary>Reads back the current log callback and its user data.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void llama_log_get(IntPtr* logCallback, void** userData);

    /// <summary>Routes every later ggml log line to a callback. Null sends them to standard error.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void ggml_log_set(delegate* unmanaged[Cdecl]<GgmlLogLevel, byte*, void*, void> logCallback, void* userData);

    /// <summary>Reads back the current ggml log callback and its user data.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void ggml_log_get(IntPtr* logCallback, void** userData);
}
