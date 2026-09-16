using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;

/// <summary>
/// The virtual table of a custom sampler: <c>struct llama_sampler_i</c>.
/// </summary>
/// <remarks>
/// Only <see cref="Apply"/> is required; the rest may be null, and the backend entries may all be null for a
/// sampler that never runs on a device. Fill this in with
/// <see langword="static"/> methods carrying <c>[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]</c>
/// and keep the instance alive for as long as the sampler exists.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct LlamaSamplerInterface
{
    /// <summary>Returns the sampler's name; may be null.</summary>
    public delegate* unmanaged[Cdecl]<LlamaSampler*, byte*> Name;

    /// <summary>Told which token was chosen; may be null.</summary>
    public delegate* unmanaged[Cdecl]<LlamaSampler*, int, void> Accept;

    /// <summary>Transforms the candidate set. Required.</summary>
    public delegate* unmanaged[Cdecl]<LlamaSampler*, LlamaTokenDataArray*, void> Apply;

    /// <summary>Returns the sampler to its initial state; may be null.</summary>
    public delegate* unmanaged[Cdecl]<LlamaSampler*, void> Reset;

    /// <summary>Copies the sampler; may be null when its context is null.</summary>
    public delegate* unmanaged[Cdecl]<LlamaSampler*, LlamaSampler*> Clone;

    /// <summary>Releases the sampler; may be null when its context is null.</summary>
    public delegate* unmanaged[Cdecl]<LlamaSampler*, void> Free;

    /// <summary>Returns true when the buffer type supports every operation the sampler needs.</summary>
    public delegate* unmanaged[Cdecl]<LlamaSampler*, void*, byte> BackendInit;

    /// <summary>Adds the accept step to the compute graph, after <see cref="BackendApply"/>.</summary>
    public delegate* unmanaged[Cdecl]<LlamaSampler*, void*, void*, void*, void> BackendAccept;

    /// <summary>Adds the sampling step to the compute graph, after <see cref="BackendInit"/>.</summary>
    public delegate* unmanaged[Cdecl]<LlamaSampler*, void*, void*, LlamaSamplerData*, void> BackendApply;

    /// <summary>Sets the graph inputs for the current micro-batch.</summary>
    public delegate* unmanaged[Cdecl]<LlamaSampler*, void> BackendSetInput;
}
