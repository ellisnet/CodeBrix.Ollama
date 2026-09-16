using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelManager; //was previously: ollama/ollama server/images.go;

/// <summary>
/// Infers what a model can do from its config, its GGUF metadata, its projectors and its Go template,
/// the way Ollama's <c>Model.Capabilities()</c> does.
/// </summary>
/// <remarks>
/// <para>
/// This is the part of the upstream inference that depends on nothing but the manifest and the files
/// it names. Upstream also consults its built-in renderers and parsers, its model-family special
/// cases and its Go template parser; none of those exist here, so none of them are ported.
/// </para>
/// <para>
/// Thinking is the one rule that is deliberately looser than upstream. Ollama decides it by parsing
/// the Go template into a syntax tree and looking for the text nodes that surround a
/// <c>.Thinking</c> field (<c>thinking.InferTags</c>), which needs a Go text/template parser. Here a
/// template that mentions a think tag or thinking at all is taken to support it.
/// </para>
/// </remarks>
internal static class ModelCapabilities
{
    /// <summary>
    /// Works out the capabilities of a model.
    /// </summary>
    /// <param name="config">The config layer, or <see langword="null"/> when the model has none.</param>
    /// <param name="metadata">
    /// The metadata of the model weights, or <see langword="null"/> when the model has no GGUF weights.
    /// </param>
    /// <param name="projectorMetadata">
    /// The metadata of each projector, or <see langword="null"/> when there are none.
    /// </param>
    /// <param name="templateText">
    /// The Go template text of the model, or <see langword="null"/> when the model has none.
    /// </param>
    /// <returns>The capabilities, in the order Ollama adds them, with no duplicates.</returns>
    public static IReadOnlyList<ModelCapability> Infer(
        ModelConfig config,
        GgufMetadata metadata,
        IReadOnlyList<GgufMetadata> projectorMetadata,
        string templateText)
    {
        var capabilities = new List<ModelCapability>();

        AddConfigCapabilities(capabilities, config);
        AddGgufCapabilities(capabilities, metadata);
        AddProjectorCapabilities(capabilities, projectorMetadata);
        AddGoTemplateCapabilities(capabilities, templateText);

        return capabilities;
    }

    /// <summary>
    /// Reports whether a GGUF chat template declares tool support, which upstream decides by looking
    /// for the words "tools" and "tool_call" anywhere in it.
    /// </summary>
    /// <param name="chatTemplate">The chat template text.</param>
    /// <returns><see langword="true"/> when the template mentions tools.</returns>
    public static bool ChatTemplateHasToolSupport(string chatTemplate)
    {
        return !string.IsNullOrEmpty(chatTemplate)
            && (chatTemplate.Contains("tools", StringComparison.Ordinal)
                || chatTemplate.Contains("tool_call", StringComparison.Ordinal));
    }

    /// <summary>
    /// Reports whether a template mentions thinking output. See the type remarks for how this differs
    /// from upstream.
    /// </summary>
    /// <param name="templateText">The template text, of either kind.</param>
    /// <returns><see langword="true"/> when the template mentions thinking.</returns>
    public static bool TemplateSupportsThinking(string templateText)
    {
        return !string.IsNullOrEmpty(templateText)
            && (templateText.Contains("<think>", StringComparison.OrdinalIgnoreCase)
                || templateText.Contains("thinking", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Adds the capabilities the config layer declares outright, which a remote model carries.
    /// </summary>
    /// <param name="capabilities">The list being built.</param>
    /// <param name="config">The config layer, or <see langword="null"/>.</param>
    private static void AddConfigCapabilities(List<ModelCapability> capabilities, ModelConfig config)
    {
        if (config == null || config.Capabilities == null)
        {
            return;
        }

        foreach (string name in config.Capabilities)
        {
            if (Enum.TryParse(name, true, out ModelCapability capability))
            {
                Add(capabilities, capability);
            }
        }
    }

    /// <summary>
    /// Adds what the model weights themselves say: the chat template's tool and thinking support, then
    /// embedding or completion, vision blocks and audio blocks.
    /// </summary>
    /// <param name="capabilities">The list being built.</param>
    /// <param name="metadata">The metadata of the model weights, or <see langword="null"/>.</param>
    private static void AddGgufCapabilities(List<ModelCapability> capabilities, GgufMetadata metadata)
    {
        if (metadata == null)
        {
            return;
        }

        string chatTemplate = metadata.ChatTemplate;
        if (!string.IsNullOrEmpty(chatTemplate))
        {
            if (ChatTemplateHasToolSupport(chatTemplate))
            {
                Add(capabilities, ModelCapability.Tools);
            }
            if (TemplateSupportsThinking(chatTemplate))
            {
                Add(capabilities, ModelCapability.Thinking);
            }
        }

        // A model that declares a pooling type produces embeddings; anything else is assumed to
        // complete text, which is exactly the assumption upstream makes.
        Add(capabilities, metadata.Has("pooling_type") ? ModelCapability.Embedding : ModelCapability.Completion);

        if (metadata.Has("vision.block_count"))
        {
            Add(capabilities, ModelCapability.Vision);
        }
        if (metadata.Has("audio.block_count"))
        {
            Add(capabilities, ModelCapability.Audio);
        }
    }

    /// <summary>
    /// Adds the capabilities the projectors bring: a projector always means vision, and one that
    /// carries an audio encoder also means audio.
    /// </summary>
    /// <param name="capabilities">The list being built.</param>
    /// <param name="projectorMetadata">The metadata of each projector, or <see langword="null"/>.</param>
    private static void AddProjectorCapabilities(
        List<ModelCapability> capabilities,
        IReadOnlyList<GgufMetadata> projectorMetadata)
    {
        if (projectorMetadata == null || projectorMetadata.Count == 0)
        {
            return;
        }

        Add(capabilities, ModelCapability.Vision);

        foreach (GgufMetadata projector in projectorMetadata)
        {
            if (ProjectorHasAudio(projector))
            {
                Add(capabilities, ModelCapability.Audio);
            }
        }
    }

    /// <summary>
    /// Adds what the Go template asks for: a template that renders tools supports tool calling, one
    /// that renders a suffix supports insertion, and one that mentions thinking supports it.
    /// </summary>
    /// <param name="capabilities">The list being built.</param>
    /// <param name="templateText">The Go template text, or <see langword="null"/>.</param>
    private static void AddGoTemplateCapabilities(List<ModelCapability> capabilities, string templateText)
    {
        if (string.IsNullOrEmpty(templateText))
        {
            return;
        }

        if (templateText.Contains(".Tools", StringComparison.Ordinal))
        {
            Add(capabilities, ModelCapability.Tools);
        }
        if (templateText.Contains(".Suffix", StringComparison.Ordinal))
        {
            Add(capabilities, ModelCapability.Insert);
        }
        if (TemplateSupportsThinking(templateText))
        {
            Add(capabilities, ModelCapability.Thinking);
        }
    }

    /// <summary>
    /// Reports whether a projector declares an audio encoder. The key is read exactly as the file
    /// spells it, because a projector qualifies it by its own architecture.
    /// </summary>
    /// <param name="projector">The projector metadata.</param>
    /// <returns><see langword="true"/> when the projector carries a true audio encoder flag.</returns>
    private static bool ProjectorHasAudio(GgufMetadata projector)
    {
        if (projector == null)
        {
            return false;
        }

        foreach (string key in projector.Keys)
        {
            if (key == null)
            {
                continue;
            }
            if (!string.Equals(key, "has_audio_encoder", StringComparison.Ordinal)
                && !key.EndsWith(".has_audio_encoder", StringComparison.Ordinal))
            {
                continue;
            }

            GgufValue value = projector.GetExactValue(key);
            if (value != null && value.RawValue is bool flag && flag)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Appends a capability unless it is already listed, which is upstream's <c>appendCapability</c>.
    /// </summary>
    /// <param name="capabilities">The list being built.</param>
    /// <param name="capability">The capability to add.</param>
    private static void Add(List<ModelCapability> capabilities, ModelCapability capability)
    {
        if (!capabilities.Contains(capability))
        {
            capabilities.Add(capability);
        }
    }
}
