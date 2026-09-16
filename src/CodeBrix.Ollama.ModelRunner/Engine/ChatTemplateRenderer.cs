using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Renders a <see cref="ChatRequest"/> into the prompt text a model sees, in whichever dialect
/// <see cref="ChatTemplateStrategy"/> resolved for it.
/// </summary>
/// <remarks>
/// <para>
/// One renderer belongs to one loaded model and holds the template text the dialect uses. The template is
/// compiled on first use rather than at load, because a model whose embedded Jinja template this library
/// cannot parse should still load and still complete raw prompts: only chat is impossible for it, and that
/// is where the failure belongs. Once compiled it is kept, so a conversation of twenty turns parses the
/// template once.
/// </para>
/// <para>
/// The native dialect is the last resort and the only one that calls into the engine: it hands the roles
/// and the message bodies to <c>llama_chat_apply_template</c>, whose template support is a fixed list of
/// families recognized by inspecting the model's own template text. It cannot express tools, so a request
/// that offers any is refused rather than quietly rendered without them.
/// </para>
/// </remarks>
internal sealed class ChatTemplateRenderer
{
    private readonly string jinjaTemplateText;
    private readonly string ollamaTemplateText;
    private readonly string nativeTemplateText;
    private readonly string bosToken;
    private readonly string eosToken;

    private JinjaTemplate jinja;
    private OllamaTemplate ollama;

    /// <summary>Creates the renderer for one loaded model.</summary>
    /// <param name="dialect">The dialect <see cref="ChatTemplateStrategy.Resolve"/> settled on.</param>
    /// <param name="jinjaTemplate">The Jinja template from the options, or <see langword="null"/>.</param>
    /// <param name="ollamaTemplate">The Ollama Go template from the options, or <see langword="null"/>.</param>
    /// <param name="embeddedTemplate">The Jinja template the model file embeds, or <see langword="null"/>.</param>
    /// <param name="bosToken">The vocabulary's beginning-of-sequence token text, or <see langword="null"/>.</param>
    /// <param name="eosToken">The vocabulary's end-of-sequence token text, or <see langword="null"/>.</param>
    public ChatTemplateRenderer(
        ChatTemplateDialect dialect,
        string jinjaTemplate,
        string ollamaTemplate,
        string embeddedTemplate,
        string bosToken,
        string eosToken)
    {
        Dialect = dialect;
        jinjaTemplateText = ChatTemplateStrategy.JinjaTemplateText(jinjaTemplate, embeddedTemplate);
        ollamaTemplateText = ollamaTemplate;
        nativeTemplateText = embeddedTemplate;
        this.bosToken = bosToken ?? string.Empty;
        this.eosToken = eosToken ?? string.Empty;

        StopStrings = dialect == ChatTemplateDialect.Ollama
            ? BuiltInStopStrings(ollamaTemplate)
            : Array.Empty<string>();
    }

    /// <summary>The dialect this renderer speaks.</summary>
    public ChatTemplateDialect Dialect { get; }

    /// <summary>
    /// The template text the reasoning tags and the tool-call literal are inferred from: the dialect's own
    /// template, and for the native dialect the model's embedded one, which is what the engine matches
    /// against. May be <see langword="null"/>.
    /// </summary>
    public string TemplateText
    {
        get
        {
            switch (Dialect)
            {
                case ChatTemplateDialect.Ollama: return ollamaTemplateText;
                case ChatTemplateDialect.Jinja: return jinjaTemplateText;
                default: return nativeTemplateText;
            }
        }
    }

    /// <summary>
    /// The stop strings that belong to the template, which is only ever non-empty for an Ollama template
    /// that is one of Ollama's own built-ins and carries them in its parameter sidecar.
    /// </summary>
    public IReadOnlyList<string> StopStrings { get; }

    /// <summary>Renders a request into the prompt the model sees.</summary>
    /// <param name="request">The request.</param>
    /// <returns>The prompt text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ChatTemplateException">The template could not be compiled or could not render this request.</exception>
    public string Render(ChatRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        switch (Dialect)
        {
            case ChatTemplateDialect.Ollama:
                return RenderOllama(request);

            case ChatTemplateDialect.Jinja:
                return RenderJinja(request);

            default:
                return RenderNative(request);
        }
    }

    /// <summary>The name of the Ollama built-in a Go template is, or <see langword="null"/> when it is not one.</summary>
    /// <param name="goTemplateText">The template text from the options.</param>
    /// <returns>The built-in's name, or <see langword="null"/>.</returns>
    /// <remarks>
    /// The comparison is on the text itself rather than on Ollama's fuzzy index, which matches a model's
    /// JINJA template to a built-in and would recognize nothing here. A caller that passed one of this
    /// library's own built-in templates through gets that built-in's stop strings; anything else gets none.
    /// </remarks>
    internal static string MatchBuiltIn(string goTemplateText)
    {
        if (string.IsNullOrWhiteSpace(goTemplateText)) return null;

        string wanted = goTemplateText.Replace("\r\n", "\n").Trim();

        foreach (string name in OllamaTemplate.BuiltInNames)
        {
            string candidate = OllamaTemplate.BuiltIn(name).Source.Replace("\r\n", "\n").Trim();
            if (string.Equals(candidate, wanted, StringComparison.Ordinal)) return name;
        }

        return null;
    }

    /// <summary>The stop strings of the built-in a Go template is, or an empty list.</summary>
    /// <param name="goTemplateText">The template text from the options.</param>
    /// <returns>The stop strings.</returns>
    private static IReadOnlyList<string> BuiltInStopStrings(string goTemplateText)
    {
        string name = MatchBuiltIn(goTemplateText);
        if (name == null) return Array.Empty<string>();

        return OllamaTemplate.BuiltInStopStrings(name);
    }

    private string RenderJinja(ChatRequest request)
    {
        if (jinja == null)
        {
            if (string.IsNullOrWhiteSpace(jinjaTemplateText))
            {
                throw new ChatTemplateException(
                    "This model has no Jinja chat template to render with. Load it with "
                    + $"{nameof(ModelRunnerOptions)}.{nameof(ModelRunnerOptions.JinjaTemplate)} set, or with "
                    + $"a different {nameof(ChatTemplateDialect)}.");
            }

            jinja = JinjaTemplate.Parse(jinjaTemplateText);
        }

        return jinja.Render(ChatJinjaVariables.Build(request, bosToken, eosToken));
    }

    private string RenderOllama(ChatRequest request)
    {
        if (ollama == null)
        {
            if (string.IsNullOrWhiteSpace(ollamaTemplateText))
            {
                throw new ChatTemplateException(
                    "This model has no Ollama chat template to render with. Load it with "
                    + $"{nameof(ModelRunnerOptions)}.{nameof(ModelRunnerOptions.OllamaTemplate)} set.");
            }

            ollama = OllamaTemplate.Parse(ollamaTemplateText);
        }

        return ollama.Render(ChatOllamaValues.Build(request));
    }

    private string RenderNative(ChatRequest request)
    {
        if (request.Tools.Count > 0)
        {
            throw new ChatTemplateException(
                $"{nameof(ChatTemplateDialect)}.{nameof(ChatTemplateDialect.Native)} cannot render tools: the "
                + "engine's built-in template support takes a role and a body per message and has nowhere to "
                + "put a tool definition. Use the Jinja dialect - the model file usually embeds a template "
                + "that does - or supply an Ollama template that renders tools.");
        }

        List<string> roles = new List<string>(request.Messages.Count);
        List<string> contents = new List<string>(request.Messages.Count);

        foreach (ChatMessage message in request.Messages)
        {
            if (message == null) continue;

            roles.Add(ChatJinjaVariables.RoleName(message.Role));
            contents.Add(message.Content ?? string.Empty);
        }

        return NativeText.ApplyChatTemplate(nativeTemplateText, roles.ToArray(), contents.ToArray(), true);
    }
}
