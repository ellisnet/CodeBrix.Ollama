using System;
using Microsoft.Win32.SafeHandles;

namespace CodeBrix.Ollama.ModelRunner; //was previously: LLamaSharp LLama/Native/SafeLLamaContextHandle.cs (reference only);

/// <summary>
/// Owns an inference context and releases it with <c>llama_free</c>.
/// </summary>
/// <remarks>A context must be released before the model it was created over.</remarks>
internal sealed class SafeLlamaContextHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    /// <summary>Creates an unowned, invalid handle for the runtime to fill in.</summary>
    public SafeLlamaContextHandle() : base(true)
    {
    }

    /// <summary>Takes ownership of a context pointer.</summary>
    /// <param name="context">The pointer returned by <c>llama_init_from_model</c>.</param>
    public SafeLlamaContextHandle(IntPtr context) : base(true)
    {
        SetHandle(context);
    }

    /// <inheritdoc />
    protected override bool ReleaseHandle()
    {
        NativeMethods.llama_free(handle);
        SetHandle(IntPtr.Zero);
        return true;
    }
}
