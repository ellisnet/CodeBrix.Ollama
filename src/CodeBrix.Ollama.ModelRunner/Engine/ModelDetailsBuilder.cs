using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Reads everything <see cref="ModelDetails"/> promises out of a loaded model handle.
/// </summary>
/// <remarks>
/// <para>
/// The same builder serves <see cref="ModelRunner.ProbeAsync"/> and <see cref="IRunningModel.Details"/>. A
/// probe normally reads the file with the engine's <c>no_alloc</c> flag, which reads the same metadata as a
/// real load and merely simulates the allocations, and its answers then match a load's field for field.
/// </para>
/// <para>
/// A file the engine will not open that way falls back to the vocabulary-only load, and that one is not the
/// same: it reads the tokenizer and the metadata but builds no tensors, so the numbers that come from the
/// tensors - the parameter count and the weights' size - come back as zero where a real load reports them.
/// <see cref="ReadOnlyTheVocabulary"/> records which of the two answered, for the caller that has to know.
/// </para>
/// </remarks>
internal static class ModelDetailsBuilder
{
    private static readonly object Marker = new object();

    private static readonly ConditionalWeakTable<ModelDetails, object> VocabularyOnlyBuilds =
        new ConditionalWeakTable<ModelDetails, object>();

    /// <summary>
    /// Whether these details describe a model the engine had opened for its vocabulary alone, whose tensor
    /// numbers - the parameter count and the weights' size - are therefore zero rather than measured.
    /// </summary>
    /// <param name="details">The details a <see cref="Build"/> returned.</param>
    /// <returns><see langword="true"/> when only the vocabulary was read.</returns>
    internal static bool ReadOnlyTheVocabulary(ModelDetails details)
    {
        object marker;
        return details != null && VocabularyOnlyBuilds.TryGetValue(details, out marker);
    }

    /// <summary>Builds the details of a loaded model, recording how the file was opened.</summary>
    /// <param name="model">The model handle.</param>
    /// <param name="path">The path the model was loaded from.</param>
    /// <param name="vocabularyOnly">Whether the engine opened the file for its vocabulary alone.</param>
    /// <returns>The details.</returns>
    public static ModelDetails Build(IntPtr model, string path, bool vocabularyOnly)
    {
        IntPtr vocab = NativeMethods.llama_model_get_vocab(model);

        ModelDetails details = new ModelDetails
        {
            Path = path,
            FileSize = FileSize(path),
            Description = NativeText.ModelDescription(model),
            Architecture = NativeText.ModelMetaValue(model, "general.architecture"),
            Name = NativeText.ModelMetaValue(model, "general.name"),
            ParameterCount = NativeMethods.llama_model_n_params(model),
            WeightsSize = NativeMethods.llama_model_size(model),
            TrainingContextLength = NativeMethods.llama_model_n_ctx_train(model),
            EmbeddingLength = NativeMethods.llama_model_n_embd(model),
            LayerCount = NativeMethods.llama_model_n_layer(model),
            HeadCount = NativeMethods.llama_model_n_head(model),
            VocabularySize = vocab == IntPtr.Zero ? 0 : NativeMethods.llama_vocab_n_tokens(vocab),
            IsHybrid = NativeMethods.llama_model_is_hybrid(model),
            IsRecurrent = NativeMethods.llama_model_is_recurrent(model),
            HasEncoder = NativeMethods.llama_model_has_encoder(model),
            HasDecoder = NativeMethods.llama_model_has_decoder(model),
            ChatTemplate = ChatTemplate(model),
            Metadata = Metadata(model),
        };

        if (vocabularyOnly) VocabularyOnlyBuilds.Add(details, Marker);

        return details;
    }

    /// <summary>Reads the Jinja chat template the file embeds.</summary>
    /// <param name="model">The model handle.</param>
    /// <returns>The template text, or <see langword="null"/> when the file carries none.</returns>
    public static unsafe string ChatTemplate(IntPtr model)
    {
        byte* text = NativeMethods.llama_model_chat_template(model, null);
        if (text == null) return null;

        string template = NativeLibraryLoader.ReadUtf8(text);
        return string.IsNullOrEmpty(template) ? null : template;
    }

    private static long FileSize(string path)
    {
        try
        {
            FileInfo info = new FileInfo(path);
            return info.Exists ? info.Length : 0L;
        }
        catch (Exception)
        {
            // A file the engine could open but this process cannot stat is not worth failing a probe over.
            return 0L;
        }
    }

    private static IReadOnlyDictionary<string, string> Metadata(IntPtr model)
    {
        Dictionary<string, string> metadata = new Dictionary<string, string>(StringComparer.Ordinal);

        int count = NativeMethods.llama_model_meta_count(model);
        for (int i = 0; i < count; i++)
        {
            string key = NativeText.ModelMetaKeyAt(model, i);
            if (string.IsNullOrEmpty(key)) continue;

            // The engine answers -1, which arrives here as null, for a key whose value is an array - the
            // token list, the merges - and those are megabytes of text that no caller of this wants.
            string value = NativeText.ModelMetaValueAt(model, i);
            if (value == null) continue;

            metadata[key] = value;
        }

        return metadata;
    }
}
