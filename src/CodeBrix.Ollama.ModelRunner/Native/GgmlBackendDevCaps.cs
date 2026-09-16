using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp ggml/include/ggml-backend.h;

/// <summary>
/// What a compute device can do: <c>struct ggml_backend_dev_caps</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GgmlBackendDevCaps
{
    /// <summary>Non-zero when the device runs operations asynchronously.</summary>
    public byte Async;

    /// <summary>Non-zero when the device can allocate pinned host buffers.</summary>
    public byte HostBuffer;

    /// <summary>Non-zero when the device can wrap a host pointer as a buffer.</summary>
    public byte BufferFromHostPtr;

    /// <summary>Non-zero when the device supports event synchronization.</summary>
    public byte Events;
}
