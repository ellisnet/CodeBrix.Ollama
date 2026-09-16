using System;
using Microsoft.Win32.SafeHandles;

namespace CodeBrix.Ollama.ModelRunner; //was previously: LLamaSharp LLama/Native/SafeLLamaSamplerHandle.cs (reference only);

/// <summary>
/// Owns a sampler, or a whole sampler chain, and releases it with <c>llama_sampler_free</c>.
/// </summary>
/// <remarks>
/// A chain takes ownership of every sampler added to it, so only the chain is wrapped in one of these; a
/// sampler handed to <c>llama_sampler_chain_add</c> must not also be owned here or it would be freed twice.
/// </remarks>
internal sealed class SafeLlamaSamplerHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    /// <summary>Creates an unowned, invalid handle for the runtime to fill in.</summary>
    public SafeLlamaSamplerHandle() : base(true)
    {
    }

    /// <summary>Takes ownership of a sampler pointer.</summary>
    /// <param name="sampler">The pointer returned by one of the <c>llama_sampler_*</c> constructors.</param>
    public SafeLlamaSamplerHandle(IntPtr sampler) : base(true)
    {
        SetHandle(sampler);
    }

    /// <inheritdoc />
    protected override bool ReleaseHandle()
    {
        NativeMethods.llama_sampler_free(handle);
        SetHandle(IntPtr.Zero);
        return true;
    }
}
