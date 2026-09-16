namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;


/// <summary>
/// The legacy per-token type recorded in older GGUF files. Mirrors <c>enum llama_token_type</c>.
/// </summary>
internal enum LlamaTokenType : int
{
    /// <summary>No type recorded.</summary>
    Undefined = 0,

    /// <summary>An ordinary text token.</summary>
    Normal = 1,

    /// <summary>The unknown-token placeholder.</summary>
    Unknown = 2,

    /// <summary>A control token.</summary>
    Control = 3,

    /// <summary>A token added by the model's author.</summary>
    UserDefined = 4,

    /// <summary>A reserved but unused slot.</summary>
    Unused = 5,

    /// <summary>A raw byte token.</summary>
    Byte = 6,
}
