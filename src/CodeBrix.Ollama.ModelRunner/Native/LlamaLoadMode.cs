namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;


/// <summary>
/// How the weights reach memory. Mirrors <c>enum llama_load_mode</c>.
/// </summary>
internal enum LlamaLoadMode : int
{
    /// <summary>Read the file normally.</summary>
    None = 0,

    /// <summary>Memory-map the file.</summary>
    Mmap = 1,

    /// <summary>Lock the weights into RAM.</summary>
    Mlock = 2,

    /// <summary>Memory-map the file and lock it into RAM.</summary>
    MmapMlock = 3,

    /// <summary>Use direct I/O where the platform has it.</summary>
    DirectIo = 4,
}
