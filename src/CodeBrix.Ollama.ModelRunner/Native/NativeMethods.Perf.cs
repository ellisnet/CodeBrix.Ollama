using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;

/// <summary>
/// The native entry points for the engine's own timers. Upstream asks third-party code to do its own
/// measurements instead; these are bound for completeness and for the timings a generation reports.
/// </summary>
internal static unsafe partial class NativeMethods
{
    /// <summary>A context's performance counters.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial LlamaPerfContextData llama_perf_context(IntPtr ctx);

    /// <summary>Prints a context's performance counters to the log.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void llama_perf_context_print(IntPtr ctx);

    /// <summary>Returns a context's performance counters to zero.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void llama_perf_context_reset(IntPtr ctx);

    /// <summary>A sampler chain's performance counters.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial LlamaPerfSamplerData llama_perf_sampler(IntPtr chain);

    /// <summary>Prints a sampler chain's performance counters to the log.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void llama_perf_sampler_print(IntPtr chain);

    /// <summary>Returns a sampler chain's performance counters to zero.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void llama_perf_sampler_reset(IntPtr chain);
}
