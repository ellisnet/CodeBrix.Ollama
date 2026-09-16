using System;
using Microsoft.Win32.SafeHandles;

namespace CodeBrix.Ollama.ModelRunner; //was previously: LLamaSharp LLama/Native/SafeLlamaModelHandle.cs (reference only);

/// <summary>
/// Owns a loaded model and releases it with <c>llama_model_free</c>.
/// </summary>
/// <remarks>
/// <para>
/// Freeing a model also frees every LoRA adapter loaded against it, so a model has to outlive its contexts
/// and its adapters. Nothing here enforces that: the engine layer keeps the order.
/// </para>
/// <para>
/// The rule this handle depends on, and cannot enforce: the imports in this binding take the model as a raw
/// <see cref="IntPtr"/>, not as this <see cref="System.Runtime.InteropServices.SafeHandle"/>. A raw pointer
/// read out of the handle carries none of the handle's lifetime with it, so the marshaller adds no reference
/// count and the collector is free to finalize the handle - and free the model - while a native call is
/// still using that pointer. The owner, which is <c>RunningModel</c>, is therefore what keeps the model
/// alive: it must hold this handle reachable, and reachable to the collector rather than merely stored in a
/// field the jitter has finished with, for the whole duration of every native call made with its pointer.
/// In practice that means the owner keeps the handle in an instance field, calls the imports from instance
/// methods on a live object, and never lets the last use of the object precede the last native call. Where a
/// call chain makes that hard to see, <c>GC.KeepAlive</c> on the owner after the native call is what makes it
/// true. The same rule applies to the context, sampler and adapter handles, whose pointers travel the same
/// way.
/// </para>
/// </remarks>
internal sealed class SafeLlamaModelHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    /// <summary>Creates an unowned, invalid handle for the runtime to fill in.</summary>
    public SafeLlamaModelHandle() : base(true)
    {
    }

    /// <summary>Takes ownership of a model pointer.</summary>
    /// <param name="model">The pointer returned by one of the <c>llama_model_load_from_*</c> functions.</param>
    public SafeLlamaModelHandle(IntPtr model) : base(true)
    {
        SetHandle(model);
    }

    /// <inheritdoc />
    protected override bool ReleaseHandle()
    {
        NativeMethods.llama_model_free(handle);
        SetHandle(IntPtr.Zero);
        return true;
    }
}
