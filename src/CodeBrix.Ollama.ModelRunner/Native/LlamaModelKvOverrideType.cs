namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;


/// <summary>
/// The kind of value a metadata override carries. Mirrors <c>enum llama_model_kv_override_type</c>.
/// </summary>
internal enum LlamaModelKvOverrideType : int
{
    /// <summary>A 64-bit signed integer.</summary>
    Int = 0,

    /// <summary>A 64-bit float.</summary>
    Float = 1,

    /// <summary>A boolean.</summary>
    Bool = 2,

    /// <summary>A string of at most 128 bytes including its terminator.</summary>
    Str = 3,
}
