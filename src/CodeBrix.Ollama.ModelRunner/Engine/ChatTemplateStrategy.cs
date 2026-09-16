using System;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Works out which chat-template dialect will render a model's chat requests, from the options and from what
/// the model file itself carries.
/// </summary>
/// <remarks>
/// <para>
/// The decision is made once, at load, so that <see cref="IRunningModel.ChatTemplateDialect"/> can answer
/// truthfully before any request is made and so that a named dialect with no template behind it fails the
/// load rather than the first chat. Nothing here renders anything: the resolution needs only the three
/// pieces of template text, which is also what makes it testable without a model.
/// </para>
/// </remarks>
internal static class ChatTemplateStrategy
{
    /// <summary>Chooses the dialect.</summary>
    /// <param name="requested">The dialect the options asked for.</param>
    /// <param name="ollamaTemplate">The Ollama Go template from the options, or <see langword="null"/>.</param>
    /// <param name="jinjaTemplate">The Jinja template from the options, or <see langword="null"/>.</param>
    /// <param name="embeddedTemplate">The Jinja template embedded in the model file, or <see langword="null"/>.</param>
    /// <returns>The dialect that will render chat requests.</returns>
    /// <exception cref="ArgumentException"><paramref name="requested"/> is not one of the named dialects.</exception>
    /// <exception cref="ModelLoadException">A dialect was named and there is no template for it.</exception>
    public static ChatTemplateDialect Resolve(
        ChatTemplateDialect requested, string ollamaTemplate, string jinjaTemplate, string embeddedTemplate)
    {
        bool hasOllama = !string.IsNullOrWhiteSpace(ollamaTemplate);
        bool hasJinja = !string.IsNullOrWhiteSpace(jinjaTemplate) || !string.IsNullOrWhiteSpace(embeddedTemplate);

        switch (requested)
        {
            case ChatTemplateDialect.Auto:
                if (hasOllama) return ChatTemplateDialect.Ollama;
                if (hasJinja) return ChatTemplateDialect.Jinja;
                return ChatTemplateDialect.Native;

            case ChatTemplateDialect.Ollama:
                if (hasOllama) return ChatTemplateDialect.Ollama;
                throw new ModelLoadException(
                    $"{nameof(ChatTemplateDialect)}.{nameof(ChatTemplateDialect.Ollama)} was asked for, but "
                    + $"{nameof(ModelRunnerOptions)}.{nameof(ModelRunnerOptions.OllamaTemplate)} is not set. "
                    + "An Ollama template is never embedded in a model file; it comes from the model's "
                    + "Modelfile, which CodeBrix.Ollama.ModelManager can supply.");

            case ChatTemplateDialect.Jinja:
                if (hasJinja) return ChatTemplateDialect.Jinja;
                throw new ModelLoadException(
                    $"{nameof(ChatTemplateDialect)}.{nameof(ChatTemplateDialect.Jinja)} was asked for, but "
                    + "the model file embeds no chat template and "
                    + $"{nameof(ModelRunnerOptions)}.{nameof(ModelRunnerOptions.JinjaTemplate)} is not set.");

            case ChatTemplateDialect.Native:
                return ChatTemplateDialect.Native;

            default:
                throw new ArgumentException(
                    $"'{requested}' is not a chat-template dialect this library knows.", nameof(requested));
        }
    }

    /// <summary>The Jinja template text a chat request should be rendered with.</summary>
    /// <param name="jinjaTemplate">The Jinja template from the options, or <see langword="null"/>.</param>
    /// <param name="embeddedTemplate">The Jinja template embedded in the model file, or <see langword="null"/>.</param>
    /// <returns>The template text, or <see langword="null"/> when there is none.</returns>
    /// <remarks>The option wins: it exists precisely to replace what the file carries.</remarks>
    public static string JinjaTemplateText(string jinjaTemplate, string embeddedTemplate)
    {
        if (!string.IsNullOrWhiteSpace(jinjaTemplate)) return jinjaTemplate;
        if (!string.IsNullOrWhiteSpace(embeddedTemplate)) return embeddedTemplate;
        return null;
    }
}
