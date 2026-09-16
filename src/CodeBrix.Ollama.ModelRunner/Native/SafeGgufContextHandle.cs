using System;
using Microsoft.Win32.SafeHandles;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp ggml/include/gguf.h;

/// <summary>
/// Owns a GGUF reader context and releases it with <c>gguf_free</c>.
/// </summary>
/// <remarks>
/// Every string and array a <c>gguf_get_*</c> call hands back belongs to this context and stops being valid
/// when it is released, so copy anything that has to outlive it.
/// </remarks>
internal sealed class SafeGgufContextHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    /// <summary>Creates an unowned, invalid handle for the runtime to fill in.</summary>
    public SafeGgufContextHandle() : base(true)
    {
    }

    /// <summary>Takes ownership of a GGUF context pointer.</summary>
    /// <param name="context">The pointer returned by one of the <c>gguf_init_*</c> functions.</param>
    public SafeGgufContextHandle(IntPtr context) : base(true)
    {
        SetHandle(context);
    }

    /// <inheritdoc />
    protected override bool ReleaseHandle()
    {
        NativeMethods.gguf_free(handle);
        SetHandle(IntPtr.Zero);
        return true;
    }
}
