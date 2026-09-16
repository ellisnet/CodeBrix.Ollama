namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;


/// <summary>
/// The sampling metadata keys the engine can name for a model. Mirrors <c>enum llama_model_meta_key</c>.
/// </summary>
internal enum LlamaModelMetaKey : int
{
    /// <summary>The recommended sampler order.</summary>
    SamplingSequence = 0,

    /// <summary>The recommended top-k.</summary>
    SamplingTopK = 1,

    /// <summary>The recommended top-p.</summary>
    SamplingTopP = 2,

    /// <summary>The recommended min-p.</summary>
    SamplingMinP = 3,

    /// <summary>The recommended XTC probability.</summary>
    SamplingXtcProbability = 4,

    /// <summary>The recommended XTC threshold.</summary>
    SamplingXtcThreshold = 5,

    /// <summary>The recommended temperature.</summary>
    SamplingTemp = 6,

    /// <summary>The recommended repetition window.</summary>
    SamplingPenaltyLastN = 7,

    /// <summary>The recommended repetition penalty.</summary>
    SamplingPenaltyRepeat = 8,

    /// <summary>The recommended mirostat version.</summary>
    SamplingMirostat = 9,

    /// <summary>The recommended mirostat tau.</summary>
    SamplingMirostatTau = 10,

    /// <summary>The recommended mirostat eta.</summary>
    SamplingMirostatEta = 11,
}
