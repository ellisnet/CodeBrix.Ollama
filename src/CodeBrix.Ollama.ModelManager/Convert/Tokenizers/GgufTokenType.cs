namespace CodeBrix.Ollama.ModelManager; //was previously: gguf-py/gguf/constants.py@b10221;

/// <summary>
/// What a vocabulary entry is, as written into <c>tokenizer.ggml.token_type</c>. The numbers are the inference
/// engine's own and never change.
/// </summary>
internal enum GgufTokenType
{
    /// <summary>An ordinary token the tokenizer may produce from text.</summary>
    Normal = 1,

    /// <summary>The token a tokenizer falls back to for text it cannot encode.</summary>
    Unknown = 2,

    /// <summary>A control token such as a beginning-of-sequence marker, never produced from text.</summary>
    Control = 3,

    /// <summary>A token the publisher added by hand, matched literally before anything else.</summary>
    UserDefined = 4,

    /// <summary>An identifier inside the vocabulary's range that the vocabulary does not use.</summary>
    Unused = 5,

    /// <summary>A single byte.</summary>
    Byte = 6
}
