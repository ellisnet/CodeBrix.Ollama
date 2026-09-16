using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;

/// <summary>
/// How a training run is set up: <c>struct llama_opt_params</c>.
/// </summary>
/// <remarks>
/// <see cref="GetOptPars"/> is <c>ggml_opt_get_optimizer_params</c>, which returns a
/// <c>ggml_opt_optimizer_params</c> structure by value. That structure belongs to ggml-opt.h, which this
/// binding does not cover, so the field is held as an opaque pointer: it can be passed through and set to
/// null, but a managed callback cannot be written for it without binding ggml-opt.h as well. Nothing in this
/// library trains a model; the field exists so the structure's layout is right.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct LlamaOptParams
{
    /// <summary>The assumed post-training context size; 0 takes the context's own.</summary>
    public uint NCtxTrain;

    /// <summary>Decides which tensors hold trainable parameters.</summary>
    public delegate* unmanaged[Cdecl]<void*, void*, byte> ParamFilter;

    /// <summary>The context pointer handed back to <see cref="ParamFilter"/>.</summary>
    public void* ParamFilterUd;

    /// <summary>Supplies the optimizer parameters; see the remarks on this type.</summary>
    public void* GetOptPars;

    /// <summary>The context pointer handed back to <see cref="GetOptPars"/>.</summary>
    public void* GetOptParsUd;

    /// <summary>Which optimizer to use.</summary>
    public GgmlOptOptimizerType OptimizerType;
}
