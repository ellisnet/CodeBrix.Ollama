using System;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;

/// <summary>
/// The per-token attribute bits a vocabulary reports. Mirrors <c>enum llama_token_attr</c>.
/// </summary>
[Flags]
internal enum LlamaTokenAttr : int
{
    /// <summary>No attributes.</summary>
    Undefined = 0,

    /// <summary>The unknown-token placeholder.</summary>
    Unknown = 1 << 0,

    /// <summary>A reserved but unused slot.</summary>
    Unused = 1 << 1,

    /// <summary>An ordinary text token.</summary>
    Normal = 1 << 2,

    /// <summary>A control token.</summary>
    Control = 1 << 3,

    /// <summary>A token added by the model's author.</summary>
    UserDefined = 1 << 4,

    /// <summary>A raw byte token.</summary>
    Byte = 1 << 5,

    /// <summary>The token's text is already normalized.</summary>
    Normalized = 1 << 6,

    /// <summary>Strip whitespace to the left of the token when rendering.</summary>
    LStrip = 1 << 7,

    /// <summary>Strip whitespace to the right of the token when rendering.</summary>
    RStrip = 1 << 8,

    /// <summary>The token only matches a whole word.</summary>
    SingleWord = 1 << 9,
}
