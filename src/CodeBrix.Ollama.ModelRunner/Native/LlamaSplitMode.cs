namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;


/// <summary>
/// How a model's weights are spread over several GPUs. Mirrors <c>enum llama_split_mode</c>.
/// </summary>
internal enum LlamaSplitMode : int
{
    /// <summary>One GPU holds everything.</summary>
    None = 0,

    /// <summary>Split layers and the key/value cache across GPUs.</summary>
    Layer = 1,

    /// <summary>Split rows across GPUs, using tensor parallelism where it is supported.</summary>
    Row = 2,

    /// <summary>Split individual tensors across GPUs.</summary>
    Tensor = 3,
}
