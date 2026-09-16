using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;

/// <summary>
/// How an inference context is created: <c>struct llama_context_params</c>, field for field and in header
/// order.
/// </summary>
/// <remarks>
/// As with <see cref="LlamaModelParams"/>, the C <c>bool</c> fields are single bytes held as
/// <see cref="byte"/> to keep the structure blittable. Start from
/// <see cref="NativeDefaults.ContextParams"/>; a zeroed structure asks for a context with no threads.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct LlamaContextParams
{
    /// <summary>The text context in tokens; 0 takes the model's trained context.</summary>
    public uint NCtx;

    /// <summary>The largest batch that may be handed to <c>llama_decode</c>.</summary>
    public uint NBatch;

    /// <summary>The largest physical micro-batch.</summary>
    public uint NUbatch;

    /// <summary>The number of distinct sequences the context can hold.</summary>
    public uint NSeqMax;

    /// <summary>Recurrent-state snapshots kept per sequence for rollback; 0 disables rollback.</summary>
    public uint NRsSeq;

    /// <summary>The largest number of outputs in one micro-batch; 0 means <see cref="NBatch"/>.</summary>
    public uint NOutputsMax;

    /// <summary>The threads used when generating one token at a time.</summary>
    public int NThreads;

    /// <summary>The threads used when processing a whole batch.</summary>
    public int NThreadsBatch;

    /// <summary>What the context is for.</summary>
    public LlamaContextType CtxType;

    /// <summary>How the rotary embedding is scaled.</summary>
    public LlamaRopeScalingType RopeScalingType;

    /// <summary>Whether embeddings are pooled by sequence.</summary>
    public LlamaPoolingType PoolingType;

    /// <summary>Which attention mask embeddings are computed with.</summary>
    public LlamaAttentionType AttentionType;

    /// <summary>When to use the flash-attention kernels.</summary>
    public LlamaFlashAttnType FlashAttnType;

    /// <summary>The rotary base frequency; 0 takes the model's.</summary>
    public float RopeFreqBase;

    /// <summary>The rotary frequency scale; 0 takes the model's.</summary>
    public float RopeFreqScale;

    /// <summary>The YaRN extrapolation mix; negative takes the model's.</summary>
    public float YarnExtFactor;

    /// <summary>The YaRN magnitude scale.</summary>
    public float YarnAttnFactor;

    /// <summary>The YaRN low correction dimension.</summary>
    public float YarnBetaFast;

    /// <summary>The YaRN high correction dimension.</summary>
    public float YarnBetaSlow;

    /// <summary>The YaRN original context size.</summary>
    public uint YarnOrigCtx;

    /// <summary>The key/value cache defragmentation threshold; upstream has deprecated it.</summary>
    public float DefragThold;

    /// <summary>A per-tensor evaluation callback for the scheduler, or null.</summary>
    public delegate* unmanaged[Cdecl]<void*, byte, void*, byte> CbEval;

    /// <summary>The context pointer handed back to <see cref="CbEval"/>.</summary>
    public void* CbEvalUserData;

    /// <summary>The element type of the key cache.</summary>
    public GgmlType TypeK;

    /// <summary>The element type of the value cache.</summary>
    public GgmlType TypeV;

    /// <summary>Returning non-zero from this aborts <c>llama_decode</c>; null disables it.</summary>
    public delegate* unmanaged[Cdecl]<void*, byte> AbortCallback;

    /// <summary>The context pointer handed back to <see cref="AbortCallback"/>.</summary>
    public void* AbortCallbackData;

    /// <summary>Non-zero to produce embeddings as well as logits.</summary>
    public byte Embeddings;

    /// <summary>Non-zero to offload the attention operations and the cache to the device.</summary>
    public byte OffloadKqv;

    /// <summary>Non-zero to skip the performance timers.</summary>
    public byte NoPerf;

    /// <summary>Non-zero to offload host tensor operations to the device.</summary>
    public byte OpOffload;

    /// <summary>Non-zero to keep a full-size sliding-window-attention cache.</summary>
    public byte SwaFull;

    /// <summary>Non-zero to share one attention buffer across the sequences.</summary>
    public byte KvUnified;

    /// <summary>The backend sampler chains, one per sequence, or null. The caller keeps them alive.</summary>
    public LlamaSamplerSeqConfig* Samplers;

    /// <summary>How many entries <see cref="Samplers"/> has.</summary>
    public nuint NSamplers;

    /// <summary>Another context to share results or memory with, or null.</summary>
    public void* CtxOther;
}
