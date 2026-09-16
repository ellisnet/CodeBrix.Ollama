namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp ggml/include/ggml-opt.h;


/// <summary>
/// Which optimizer a training run uses. Mirrors <c>enum ggml_opt_optimizer_type</c>.
/// </summary>
internal enum GgmlOptOptimizerType : int
{
    /// <summary>AdamW.</summary>
    AdamW = 0,

    /// <summary>Stochastic gradient descent.</summary>
    Sgd = 1,
}
