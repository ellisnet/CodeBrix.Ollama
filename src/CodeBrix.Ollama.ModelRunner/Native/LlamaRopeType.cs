namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;


/// <summary>
/// Which rotary position embedding a model uses. Mirrors <c>enum llama_rope_type</c>.
/// </summary>
internal enum LlamaRopeType : int
{
    /// <summary>The model does not use rotary position embedding.</summary>
    None = -1,

    /// <summary>The original interleaved layout.</summary>
    Norm = 0,

    /// <summary>The GPT-NeoX half-split layout (<c>GGML_ROPE_TYPE_NEOX</c>).</summary>
    Neox = 2,

    /// <summary>Multimodal rotary embedding (<c>GGML_ROPE_TYPE_MROPE</c>).</summary>
    MRope = 8,

    /// <summary>Interleaved multimodal rotary embedding (<c>GGML_ROPE_TYPE_IMROPE</c>).</summary>
    IMRope = 40,

    /// <summary>The vision-tower layout (<c>GGML_ROPE_TYPE_VISION</c>).</summary>
    Vision = 24,
}
