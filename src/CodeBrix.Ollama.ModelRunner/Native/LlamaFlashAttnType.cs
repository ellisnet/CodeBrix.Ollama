namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;


/// <summary>
/// When a context uses the flash-attention kernels. Mirrors <c>enum llama_flash_attn_type</c>.
/// </summary>
internal enum LlamaFlashAttnType : int
{
    /// <summary>Let the engine decide.</summary>
    Auto = -1,

    /// <summary>Never use flash attention.</summary>
    Disabled = 0,

    /// <summary>Always use flash attention.</summary>
    Enabled = 1,
}
