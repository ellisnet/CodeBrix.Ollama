using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;

/// <summary>
/// The native entry points: backend start-up, the platform capability queries, the default parameter
/// structures, the compute-device list and the CPU feature flags.
/// </summary>
internal static unsafe partial class NativeMethods
{
    /// <summary>Initializes the llama and ggml backends. Call once before anything else.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void llama_backend_init();

    /// <summary>Releases the backends. Not required before process exit.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void llama_backend_free();

    /// <summary>Applies a NUMA strategy to the CPU backend.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void llama_numa_init(GgmlNumaStrategy numa);

    /// <summary>The engine's monotonic clock, in microseconds.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial long llama_time_us();

    /// <summary>The largest number of devices the engine will use.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial nuint llama_max_devices();

    /// <summary>The largest number of sequences a context may hold.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial nuint llama_max_parallel_sequences();

    /// <summary>The largest number of tensor buffer-type overrides a model may carry.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial nuint llama_max_tensor_buft_overrides();

    /// <summary>Whether this build supports memory-mapped loading.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool llama_supports_mmap();

    /// <summary>Whether this build supports locking the weights into RAM.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool llama_supports_mlock();

    /// <summary>Whether this build supports offloading to a GPU.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool llama_supports_gpu_offload();

    /// <summary>Whether this build supports the RPC backend.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool llama_supports_rpc();

    /// <summary>The engine's system-information line, owned by the library.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial byte* llama_print_system_info();

    /// <summary>Attaches explicit thread pools to a context.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void llama_attach_threadpool(IntPtr ctx, IntPtr threadpool, IntPtr threadpoolBatch);

    /// <summary>Detaches the thread pools from a context.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void llama_detach_threadpool(IntPtr ctx);

    /// <summary>Builds the file name of one shard of a split model; returns its length.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_split_path(byte* splitPath, nuint maxlen, string pathPrefix, int splitNo, int splitCount);

    /// <summary>Extracts the prefix from a split model's file name; returns its length.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_split_prefix(byte* splitPrefix, nuint maxlen, string splitPath, int splitNo, int splitCount);

    /// <summary>Fills an array with the names of the built-in chat templates; returns how many there are.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_chat_builtin_templates(byte** output, nuint len);

    /// <summary>The name of a flash-attention setting, owned by the library.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial byte* llama_flash_attn_type_name(LlamaFlashAttnType flashAttnType);

    /// <summary>The name of a file type, for example "Q8_0", owned by the library.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial byte* llama_ftype_name(LlamaFtype ftype);

    /// <summary>The name of a load mode, owned by the library.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial byte* llama_load_mode_name(LlamaLoadMode loadMode);

    /// <summary>Parses a load mode from its name.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial LlamaLoadMode llama_load_mode_from_str(string str);

    /// <summary>The model parameters the library considers the defaults.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial LlamaModelParams llama_model_default_params();

    /// <summary>The context parameters the library considers the defaults.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial LlamaContextParams llama_context_default_params();

    /// <summary>The sampler-chain parameters the library considers the defaults.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial LlamaSamplerChainParams llama_sampler_chain_default_params();

    /// <summary>The quantization parameters the library considers the defaults.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial LlamaModelQuantizeParams llama_model_quantize_default_params();

    /// <summary>How many compute devices the engine can see.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial nuint ggml_backend_dev_count();

    /// <summary>The device at an index, or zero when the index is out of range.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr ggml_backend_dev_get(nuint index);

    /// <summary>Finds a device by name, or zero.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr ggml_backend_dev_by_name(string name);

    /// <summary>Finds the first device of a kind, or zero.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr ggml_backend_dev_by_type(GgmlBackendDevType type);

    /// <summary>A device's short name, owned by the engine.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial byte* ggml_backend_dev_name(IntPtr device);

    /// <summary>A device's description, owned by the engine.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial byte* ggml_backend_dev_description(IntPtr device);

    /// <summary>A device's free and total memory in bytes.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void ggml_backend_dev_memory(IntPtr device, nuint* free, nuint* total);

    /// <summary>The kind of a device.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial GgmlBackendDevType ggml_backend_dev_type(IntPtr device);

    /// <summary>Everything a device reports about itself, in one call.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void ggml_backend_dev_get_props(IntPtr device, GgmlBackendDevProps* props);

    /// <summary>Whether the CPU backend was built with, and the processor has, AMX INT8.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int ggml_cpu_has_amx_int8();

    /// <summary>Whether the CPU backend was built with, and the processor has, ARM FMA.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int ggml_cpu_has_arm_fma();

    /// <summary>Whether the CPU backend was built with, and the processor has, AVX.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int ggml_cpu_has_avx();

    /// <summary>Whether the CPU backend was built with, and the processor has, AVX VNNI.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int ggml_cpu_has_avx_vnni();

    /// <summary>Whether the CPU backend was built with, and the processor has, AVX2.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int ggml_cpu_has_avx2();

    /// <summary>Whether the CPU backend was built with, and the processor has, AVX512.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int ggml_cpu_has_avx512();

    /// <summary>Whether the CPU backend was built with, and the processor has, AVX512 BF16.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int ggml_cpu_has_avx512_bf16();

    /// <summary>Whether the CPU backend was built with, and the processor has, AVX512 VBMI.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int ggml_cpu_has_avx512_vbmi();

    /// <summary>Whether the CPU backend was built with, and the processor has, AVX512 VNNI.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int ggml_cpu_has_avx512_vnni();

    /// <summary>Whether the CPU backend was built with, and the processor has, BMI2.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int ggml_cpu_has_bmi2();

    /// <summary>Whether the CPU backend was built with, and the processor has, DOTPROD.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int ggml_cpu_has_dotprod();

    /// <summary>Whether the CPU backend was built with, and the processor has, F16C.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int ggml_cpu_has_f16c();

    /// <summary>Whether the CPU backend was built with, and the processor has, FMA.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int ggml_cpu_has_fma();

    /// <summary>Whether the CPU backend was built with, and the processor has, FP16 VA.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int ggml_cpu_has_fp16_va();

    /// <summary>Whether the CPU backend was built with, and the processor has, LLAMAFILE.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int ggml_cpu_has_llamafile();

    /// <summary>Whether the CPU backend was built with, and the processor has, MATMUL INT8.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int ggml_cpu_has_matmul_int8();

    /// <summary>Whether the CPU backend was built with, and the processor has, NEON.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int ggml_cpu_has_neon();

    /// <summary>Whether the CPU backend was built with, and the processor has, RISCV V.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int ggml_cpu_has_riscv_v();

    /// <summary>Whether the CPU backend was built with, and the processor has, SME.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int ggml_cpu_has_sme();

    /// <summary>Whether the CPU backend was built with, and the processor has, SME2.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int ggml_cpu_has_sme2();

    /// <summary>Whether the CPU backend was built with, and the processor has, SSE3.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int ggml_cpu_has_sse3();

    /// <summary>Whether the CPU backend was built with, and the processor has, SSSE3.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int ggml_cpu_has_ssse3();

    /// <summary>Whether the CPU backend was built with, and the processor has, SVE.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int ggml_cpu_has_sve();

    /// <summary>Whether the CPU backend was built with, and the processor has, VSX.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int ggml_cpu_has_vsx();

    /// <summary>Whether the CPU backend was built with, and the processor has, VXE.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int ggml_cpu_has_vxe();

    /// <summary>Whether the CPU backend was built with, and the processor has, WASM SIMD.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int ggml_cpu_has_wasm_simd();
}
