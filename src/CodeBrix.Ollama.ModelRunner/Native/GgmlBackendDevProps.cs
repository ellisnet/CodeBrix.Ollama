using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp ggml/include/ggml-backend.h;

/// <summary>
/// Everything a compute device reports about itself: <c>struct ggml_backend_dev_props</c>.
/// </summary>
/// <remarks>The three string fields are NUL-terminated UTF-8 owned by the engine; never free them.</remarks>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GgmlBackendDevProps
{
    /// <summary>The device's short name.</summary>
    public byte* Name;

    /// <summary>The device's description.</summary>
    public byte* Description;

    /// <summary>The device's free memory in bytes.</summary>
    public nuint MemoryFree;

    /// <summary>The device's total memory in bytes.</summary>
    public nuint MemoryTotal;

    /// <summary>The kind of device.</summary>
    public GgmlBackendDevType Type;

    /// <summary>The device's identifier, for PCI devices the lower-case bus id, or null when unknown.</summary>
    public byte* DeviceId;

    /// <summary>What the device can do.</summary>
    public GgmlBackendDevCaps Caps;
}
