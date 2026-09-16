namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;


/// <summary>
/// How a context extends a model's trained rotary embedding. Mirrors <c>enum llama_rope_scaling_type</c>.
/// </summary>
internal enum LlamaRopeScalingType : int
{
    /// <summary>Take the model's own setting.</summary>
    Unspecified = -1,

    /// <summary>No scaling.</summary>
    None = 0,

    /// <summary>Linear position interpolation.</summary>
    Linear = 1,

    /// <summary>YaRN scaling.</summary>
    Yarn = 2,

    /// <summary>LongRoPE scaling.</summary>
    LongRope = 3,
}
