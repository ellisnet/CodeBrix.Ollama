namespace ModelQueryTool.ModelAccess.Models;

/// <summary>
/// The models this library ships a description of. The application uses one of them and never asks a
/// person to choose.
/// </summary>
public static class KnownModels
{
    /// <summary>
    /// The name Hugging Face serves the model under through the registry protocol a plain pull speaks.
    /// </summary>
    public const string Qwen35Name = "hf.co/unsloth/Qwen3.5-35B-A3B-GGUF:Q4_K_M";

    /// <summary>
    /// The name to show a person, which says both what the model is and how far its weights are
    /// quantized.
    /// </summary>
    public const string Qwen35DisplayName = "Qwen 3.5 35B-A3B (Q4_K_M)";

    /// <summary>
    /// Every byte of the model as the publisher serves it: the weights, the vision projector and the
    /// configuration together.
    /// </summary>
    public const long Qwen35TotalBytes = 22_915_307_304L;

    /// <summary>
    /// The digest the model-weights layer carried when the publisher was last read. It is reported and
    /// never enforced; a re-upload changes it.
    /// </summary>
    public const string Qwen35WeightsSha256 =
        "3b46d1066bc91cc2d613e3bc22ce691dd77e6f0d33c9060690d24ce6de494375";

    private static readonly ModelDescriptor Qwen35Descriptor = new ModelDescriptor(
        Qwen35Name,
        Qwen35DisplayName,
        Qwen35TotalBytes,
        Qwen35WeightsSha256);

    /// <summary>
    /// Gets the model the application talks to: the whole publication, the vision projector included,
    /// so that nothing about obtaining it has to be done twice when images are added later.
    /// </summary>
    public static ModelDescriptor Qwen35
    {
        get { return Qwen35Descriptor; }
    }
}
