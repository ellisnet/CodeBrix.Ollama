namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The media type strings Ollama uses in manifests. A manifest's config layer and each of its content
/// layers carries one of these; the store reads and writes them verbatim so a store directory is
/// interchangeable with a real Ollama install.
/// </summary>
public static class MediaTypes
{
    /// <summary>The manifest document itself (Docker distribution schema 2).</summary>
    public const string Manifest = "application/vnd.docker.distribution.manifest.v2+json";

    /// <summary>The config layer: a small JSON document describing format, family, parameter size and quantization.</summary>
    public const string Config = "application/vnd.docker.container.image.v1+json";

    /// <summary>A GGUF model weights file. A split model carries several of these in order.</summary>
    public const string Model = "application/vnd.ollama.image.model";

    /// <summary>A GGUF multimodal projector (vision or audio encoder).</summary>
    public const string Projector = "application/vnd.ollama.image.projector";

    /// <summary>A GGUF LoRA adapter.</summary>
    public const string Adapter = "application/vnd.ollama.image.adapter";

    /// <summary>A GGUF speculative-decoding draft model.</summary>
    public const string Draft = "application/vnd.ollama.image.draft";

    /// <summary>The prompt template text (Go text/template syntax as Ollama uses it).</summary>
    public const string Template = "application/vnd.ollama.image.template";

    /// <summary>The legacy name for <see cref="Template"/>; read but never written.</summary>
    public const string Prompt = "application/vnd.ollama.image.prompt";

    /// <summary>The system prompt text.</summary>
    public const string System = "application/vnd.ollama.image.system";

    /// <summary>A JSON object of default sampling and runtime parameters.</summary>
    public const string Params = "application/vnd.ollama.image.params";

    /// <summary>A JSON array of preset conversation messages.</summary>
    public const string Messages = "application/vnd.ollama.image.messages";

    /// <summary>License text. A manifest may carry several.</summary>
    public const string License = "application/vnd.ollama.image.license";

    /// <summary>A single safetensors tensor (models in this format are not runnable here and are never pulled).</summary>
    public const string Tensor = "application/vnd.ollama.image.tensor";

    /// <summary>A JSON configuration file accompanying a safetensors model.</summary>
    public const string Json = "application/vnd.ollama.image.json";

    /// <summary>Deprecated embeddings layer; read and ignored.</summary>
    public const string Embed = "application/vnd.ollama.image.embed";
}
