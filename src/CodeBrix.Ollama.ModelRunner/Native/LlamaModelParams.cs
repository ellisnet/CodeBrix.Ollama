using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;

/// <summary>
/// How a model file is loaded: <c>struct llama_model_params</c>, field for field and in header order.
/// </summary>
/// <remarks>
/// The C <c>bool</c> fields are one byte each and are held here as <see cref="byte"/> so the whole structure
/// stays blittable and can be passed by value through a <c>LibraryImport</c> signature. Fetch a filled-in
/// instance from <see cref="NativeDefaults.ModelParams"/> rather than starting from a zeroed one: several of
/// the defaults are not zero.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct LlamaModelParams
{
    /// <summary>A null-terminated list of <c>ggml_backend_dev_t</c> to offload to, or null for all of them.</summary>
    public void** Devices;

    /// <summary>A null-terminated list of buffer-type overrides matched by tensor-name pattern, or null.</summary>
    public LlamaModelTensorBuftOverride* TensorBuftOverrides;

    /// <summary>How many layers to keep in device memory; a negative value means all of them.</summary>
    public int NGpuLayers;

    /// <summary>How the model is split over several GPUs.</summary>
    public LlamaSplitMode SplitMode;

    /// <summary>How the weights reach memory.</summary>
    public LlamaLoadMode LoadMode;

    /// <summary>The GPU that holds the whole model when <see cref="SplitMode"/> is <see cref="LlamaSplitMode.None"/>.</summary>
    public int MainGpu;

    /// <summary>The share of the model to place on each GPU, <c>llama_max_devices()</c> floats, or null.</summary>
    public float* TensorSplit;

    /// <summary>Called with a fraction between 0 and 1; returning false aborts the load. Null disables it.</summary>
    public delegate* unmanaged[Cdecl]<float, void*, byte> ProgressCallback;

    /// <summary>The context pointer handed back to <see cref="ProgressCallback"/>.</summary>
    public void* ProgressCallbackUserData;

    /// <summary>A null-terminated list of metadata overrides, or null.</summary>
    public LlamaModelKvOverride* KvOverrides;

    /// <summary>Non-zero to read only the vocabulary and no weights.</summary>
    public byte VocabOnly;

    /// <summary>Non-zero to validate every tensor's data while loading.</summary>
    public byte CheckTensors;

    /// <summary>Non-zero to allow the extra buffer types used for weight repacking.</summary>
    public byte UseExtraBufts;

    /// <summary>Non-zero to bypass host buffers so extra buffers can be used.</summary>
    public byte NoHost;

    /// <summary>Non-zero to read metadata only and merely simulate the allocations.</summary>
    public byte NoAlloc;

    /// <summary>Non-zero to load the multi-token-prediction layers.</summary>
    public byte LoadMtp;
}
