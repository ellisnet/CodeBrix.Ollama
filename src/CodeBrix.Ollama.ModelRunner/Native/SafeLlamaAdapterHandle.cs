using System;
using Microsoft.Win32.SafeHandles;

namespace CodeBrix.Ollama.ModelRunner; //was previously: LLamaSharp LLama/Native/LoraAdapter.cs (reference only);

/// <summary>
/// Owns a LoRA adapter and releases it with <c>llama_adapter_lora_free</c>.
/// </summary>
/// <remarks>
/// <para>
/// The header is explicit about the ownership: an adapter is valid only for as long as the model it was
/// loaded against, and one that is never freed by hand is freed when that model is. Both facts matter here.
/// </para>
/// <para>
/// A handle that is released while its model is still alive is the ordinary case and is what this type does.
/// A handle whose model has already been freed must NOT be released again, because the adapter is gone with
/// it - call <see cref="SuppressRelease"/> on every adapter of a model before freeing the model, and the
/// finalizer will leave the dangling pointer alone.
/// </para>
/// </remarks>
internal sealed class SafeLlamaAdapterHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    /// <summary>Creates an unowned, invalid handle for the runtime to fill in.</summary>
    public SafeLlamaAdapterHandle() : base(true)
    {
    }

    /// <summary>Takes ownership of an adapter pointer.</summary>
    /// <param name="adapter">The pointer returned by <c>llama_adapter_lora_init</c>.</param>
    public SafeLlamaAdapterHandle(IntPtr adapter) : base(true)
    {
        SetHandle(adapter);
    }

    /// <summary>
    /// Forgets the adapter without freeing it, for when its model has already been freed and has taken the
    /// adapter with it.
    /// </summary>
    public void SuppressRelease()
    {
        SetHandleAsInvalid();
    }

    /// <inheritdoc />
    protected override bool ReleaseHandle()
    {
        NativeMethods.llama_adapter_lora_free(handle);
        SetHandle(IntPtr.Zero);
        return true;
    }
}
