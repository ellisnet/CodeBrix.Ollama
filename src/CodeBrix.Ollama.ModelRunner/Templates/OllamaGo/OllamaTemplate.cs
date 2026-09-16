using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ollama/ollama template/template.go;

/// <summary>
/// An Ollama chat template - the Go text/template a model's Modelfile TEMPLATE holds, which
/// CodeBrix.Ollama.ModelManager returns as text - parsed and ready to render a chat request into the
/// prompt the model sees.
/// </summary>
/// <remarks>
/// This is a port of Ollama's own template layer, template language and all, so a model pulled from the
/// Ollama registry is prompted exactly as Ollama prompts it. That includes the two shapes a template can
/// take: the modern one that ranges over <c>.Messages</c>, and the older one built from <c>.System</c>,
/// <c>.Prompt</c> and <c>.Response</c>, which is rendered once per exchange and truncated after the last
/// <c>.Response</c>.
/// </remarks>
public sealed class OllamaTemplate
{
    private const string LegacySystem = "System";
    private const string LegacyPrompt = "Prompt";
    private const string LegacyResponse = "Response";

    private readonly GoTemplate _template;

    private OllamaTemplate(string source, GoTemplate template)
    {
        Source = source;
        _template = template;
        Variables = CollectVariables(template);
    }

    /// <summary>The template text this was parsed from.</summary>
    public string Source { get; }

    /// <summary>
    /// The lower-cased names of the fields and variables the template refers to, in alphabetical order.
    /// Ollama uses this to tell a <c>.Messages</c> template from an older one; so does
    /// <see cref="Render"/>.
    /// </summary>
    public IReadOnlyList<string> Variables { get; }

    /// <summary>The names of Ollama's built-in templates, in alphabetical order.</summary>
    public static IReadOnlyList<string> BuiltInNames => OllamaTemplateBuiltIns.Names;

    /// <summary>The parsed Go template, for the layers that read the parse tree rather than render it.</summary>
    internal GoTemplate GoTemplate => _template;

    /// <summary>
    /// Parses an Ollama chat template.
    /// </summary>
    /// <param name="text">The template text.</param>
    /// <returns>The parsed template.</returns>
    /// <exception cref="ChatTemplateException">The template text is not a valid Go text/template, or it
    /// invokes a template with no pipeline at all, which Ollama rejects while it collects the variables.
    /// A <c>{{template "name" .}}</c> that names a template the text never defines parses cleanly; that
    /// is only noticed by <see cref="Render"/>.</exception>
    public static OllamaTemplate Parse(string text)
    {
        GoTemplate template = GoTemplate.Parse(string.Empty, text ?? string.Empty, OllamaTemplateFuncs.Funcs);
        IReadOnlyList<string> variables = CollectVariables(template);
        if (!variables.Contains("messages") && !variables.Contains("response"))
        {
            //touch up the template the way Ollama does and append {{ .Response }}
            template.Root.Append(ResponseAction());
        }

        return new OllamaTemplate(text ?? string.Empty, template);
    }

    /// <summary>
    /// Returns the built-in template Ollama would pick for an arbitrary chat template - typically the
    /// Jinja <c>chat_template</c> of a model file - by finding the closest entry in Ollama's index.
    /// </summary>
    /// <param name="text">The chat template text to match.</param>
    /// <returns>The name of the matching built-in, or <see langword="null"/> when nothing is close enough.</returns>
    public static string NamedBuiltIn(string text)
    {
        string best = null;
        int score = int.MaxValue;
        foreach (OllamaNamedTemplate candidate in OllamaTemplateBuiltIns.Index)
        {
            int distance = GoLevenshtein.ComputeDistance(text ?? string.Empty, candidate.Template ?? string.Empty);
            if (distance < score)
            {
                score = distance;
                best = candidate.Name;
            }
        }

        return score < 100 ? best : null;
    }

    /// <summary>
    /// Parses one of Ollama's built-in templates.
    /// </summary>
    /// <param name="name">The name of the built-in, as it appears in <see cref="BuiltInNames"/>.</param>
    /// <returns>The parsed template.</returns>
    /// <exception cref="ChatTemplateException">There is no built-in template with that name.</exception>
    public static OllamaTemplate BuiltIn(string name)
    {
        string text = name == null ? null : OllamaTemplateBuiltIns.ReadTemplate(name);
        if (text == null)
        {
            throw new ChatTemplateException("no built-in Ollama template named " + (name ?? "<null>"));
        }

        return Parse(text);
    }

    /// <summary>
    /// Returns the stop strings that accompany one of Ollama's built-in templates.
    /// </summary>
    /// <param name="name">The name of the built-in, as it appears in <see cref="BuiltInNames"/>.</param>
    /// <returns>The stop strings, which is empty when the built-in has none.</returns>
    public static IReadOnlyList<string> BuiltInStopStrings(string name)
        => OllamaTemplateBuiltIns.ReadStopParameters(name ?? string.Empty);

    /// <summary>
    /// Renders a chat request into the prompt text the model sees.
    /// </summary>
    /// <param name="values">The conversation and the settings to render with.</param>
    /// <returns>The rendered prompt.</returns>
    /// <exception cref="ChatTemplateException">The template could not be rendered.</exception>
    public string Render(OllamaTemplateValues values)
    {
        OllamaTemplateValues v = values ?? new OllamaTemplateValues();
        Collate(v, out string system, out List<ChatMessage> messages);

        bool think = v.Think ?? false;
        string thinkLevel = v.ThinkLevel ?? string.Empty;
        bool isThinkSet = v.Think.HasValue;

        if (!string.IsNullOrEmpty(v.Prompt) && !string.IsNullOrEmpty(v.Suffix))
        {
            GoMap suffixData = NewData(think, thinkLevel, isThinkSet);
            suffixData.Set(LegacyPrompt, v.Prompt);
            suffixData.Set("Suffix", v.Suffix);
            suffixData.Set(LegacyResponse, string.Empty);
            return _template.Execute(suffixData);
        }

        if (!v.ForceLegacy && Variables.Contains("messages"))
        {
            GoMap data = NewData(think, thinkLevel, isThinkSet);
            data.Set(LegacySystem, system);
            data.Set("Messages", OllamaTemplateBinder.Messages(messages));
            data.Set("Tools", OllamaTemplateBinder.Tools(ToReadOnly(v.Tools)));
            data.Set(LegacyResponse, string.Empty);
            return _template.Execute(data);
        }

        return RenderLegacy(messages, think, thinkLevel, isThinkSet);
    }

    private string RenderLegacy(List<ChatMessage> messages, bool think, string thinkLevel, bool isThinkSet)
    {
        StringBuilder output = new StringBuilder();
        string system = string.Empty;
        string prompt = string.Empty;
        string response = string.Empty;

        foreach (ChatMessage message in messages)
        {
            switch (message.Role)
            {
                case ChatRole.System:
                    if (prompt.Length > 0 || response.Length > 0)
                    {
                        output.Append(_template.Execute(LegacyData(system, prompt, response, think, thinkLevel,
                            isThinkSet)));
                        system = string.Empty;
                        prompt = string.Empty;
                        response = string.Empty;
                    }

                    system = message.Content ?? string.Empty;
                    break;
                case ChatRole.User:
                    if (response.Length > 0)
                    {
                        output.Append(_template.Execute(LegacyData(system, prompt, response, think, thinkLevel,
                            isThinkSet)));
                        system = string.Empty;
                        prompt = string.Empty;
                        response = string.Empty;
                    }

                    prompt = message.Content ?? string.Empty;
                    break;
                case ChatRole.Assistant:
                    response = message.Content ?? string.Empty;
                    break;
            }
        }

        GoListNode truncated = TruncateAfterResponse(_template.Root.CopyList());
        GoTemplate tail = GoTemplate.FromRoot(string.Empty, truncated, _template.Funcs);
        output.Append(tail.Execute(LegacyData(system, prompt, response, think, thinkLevel, isThinkSet)));
        return output.ToString();
    }

    private static GoMap LegacyData(string system, string prompt, string response, bool think, string thinkLevel,
        bool isThinkSet)
    {
        GoMap data = NewData(think, thinkLevel, isThinkSet);
        data.Set(LegacySystem, system);
        data.Set(LegacyPrompt, prompt);
        data.Set(LegacyResponse, response);
        return data;
    }

    private static GoMap NewData(bool think, string thinkLevel, bool isThinkSet)
    {
        GoMap data = new GoMap();
        data.Set("Think", think);
        data.Set("ThinkLevel", thinkLevel);
        data.Set("IsThinkSet", isThinkSet);
        return data;
    }

    private static IReadOnlyList<ToolDefinition> ToReadOnly(IList<ToolDefinition> tools)
    {
        if (tools == null)
        {
            return null;
        }

        List<ToolDefinition> list = new List<ToolDefinition>(tools.Count);
        foreach (ToolDefinition tool in tools)
        {
            if (tool != null)
            {
                list.Add(tool);
            }
        }

        return list;
    }

    /// <summary>
    /// Merges consecutive messages of the same role (tool messages excepted, which keep their own
    /// metadata) and collects the system messages, exactly as Ollama's <c>collate</c> does.
    /// </summary>
    /// <param name="values">The values being rendered.</param>
    /// <param name="system">The system messages joined by blank lines.</param>
    /// <param name="messages">The merged messages.</param>
    internal static void Collate(OllamaTemplateValues values, out string system, out List<ChatMessage> messages)
    {
        List<ChatMessage> source = new List<ChatMessage>();
        if (!string.IsNullOrEmpty(values.System))
        {
            source.Add(new ChatMessage(ChatRole.System, values.System));
        }

        if (values.Messages != null)
        {
            foreach (ChatMessage message in values.Messages)
            {
                if (message != null)
                {
                    source.Add(message);
                }
            }
        }

        List<string> systems = new List<string>();
        List<ChatMessage> collated = new List<ChatMessage>();
        foreach (ChatMessage message in source)
        {
            if (message.Role == ChatRole.System)
            {
                systems.Add(message.Content ?? string.Empty);
            }

            if (collated.Count > 0 && collated[collated.Count - 1].Role == message.Role
                && message.Role != ChatRole.Tool)
            {
                ChatMessage last = collated[collated.Count - 1];
                last.Content = (last.Content ?? string.Empty) + "\n\n" + (message.Content ?? string.Empty);
            }
            else
            {
                collated.Add(Clone(message));
            }
        }

        system = string.Join("\n\n", systems);
        messages = collated;
    }

    private static ChatMessage Clone(ChatMessage message)
        => new ChatMessage
        {
            Role = message.Role,
            Content = message.Content ?? string.Empty,
            Thinking = message.Thinking,
            ToolCalls = message.ToolCalls,
            ToolCallId = message.ToolCallId,
            ToolName = message.ToolName,
        };

    private static GoActionNode ResponseAction()
    {
        GoCommandNode command = new GoCommandNode(0);
        command.Append(new GoFieldNode(0, "." + LegacyResponse));
        GoPipeNode pipe = new GoPipeNode(0, 0);
        pipe.Append(command);
        return new GoActionNode(0, 0, pipe);
    }

    private static IReadOnlyList<string> CollectVariables(GoTemplate template)
    {
        List<string> names = new List<string>();
        foreach (KeyValuePair<string, GoTemplateTree> tree in template.Trees)
        {
            if (tree.Value.Root == null)
            {
                continue;
            }

            foreach (GoNode node in tree.Value.Root.Nodes)
            {
                Identifiers(node, names);
            }
        }

        SortedSet<string> set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string name in names)
        {
            set.Add(name.ToLowerInvariant());
        }

        return new List<string>(set);
    }

    private static void Identifiers(GoNode node, List<string> names)
    {
        switch (node)
        {
            case GoListNode list:
                foreach (GoNode child in list.Nodes)
                {
                    Identifiers(child, names);
                }

                return;
            case GoTemplateCallNode call:
                //upstream Ollama returns this as an error from Identifiers, which fails its Parse, so
                //"{{template \"name\"}}" without a pipeline is a parse-time failure there and here
                if (call.Pipe == null)
                {
                    throw new ChatTemplateException("undefined template specified");
                }

                Identifiers(call.Pipe, names);
                return;
            case GoActionNode action:
                if (action.Pipe == null)
                {
                    throw new ChatTemplateException("undefined action in template");
                }

                Identifiers(action.Pipe, names);
                return;
            case GoBranchNode branch:
                if (branch.Pipe == null)
                {
                    throw new ChatTemplateException("undefined branch");
                }

                Identifiers(branch.Pipe, names);
                if (branch.List != null)
                {
                    Identifiers(branch.List, names);
                }

                if (branch.ElseList != null)
                {
                    Identifiers(branch.ElseList, names);
                }

                return;
            case GoPipeNode pipe:
                foreach (GoCommandNode command in pipe.Cmds)
                {
                    foreach (GoNode arg in command.Args)
                    {
                        Identifiers(arg, names);
                    }
                }

                return;
            case GoFieldNode field:
                names.AddRange(field.Ident);
                return;
            case GoVariableNode variable:
                //upstream Ollama's Identifiers has this case too, so a template that assigns a system
                //message to $system still counts as a legacy template
                names.AddRange(variable.Ident);
                return;
            default:
                return;
        }
    }

    private static GoListNode TruncateAfterResponse(GoListNode root)
    {
        bool cut = false;
        GoNode Walk(GoNode node)
        {
            if (cut)
            {
                return null;
            }

            if (node is GoFieldNode field && Array.IndexOf(field.Ident, LegacyResponse) >= 0)
            {
                cut = true;
                return node;
            }

            switch (node)
            {
                case GoListNode list:
                {
                    List<GoNode> kept = new List<GoNode>();
                    foreach (GoNode child in list.Nodes)
                    {
                        GoNode walked = Walk(child);
                        if (walked != null)
                        {
                            kept.Add(walked);
                        }
                    }

                    list.Nodes = kept;
                    return list;
                }

                case GoBranchNode branch:
                {
                    branch.List = (GoListNode)Walk(branch.List);
                    if (branch.ElseList != null)
                    {
                        branch.ElseList = (GoListNode)Walk(branch.ElseList);
                    }

                    return branch;
                }

                case GoActionNode action:
                {
                    GoNode walked = Walk(action.Pipe);
                    if (walked == null)
                    {
                        return null;
                    }

                    action.Pipe = (GoPipeNode)walked;
                    return action;
                }

                case GoPipeNode pipe:
                {
                    List<GoCommandNode> commands = new List<GoCommandNode>();
                    foreach (GoCommandNode command in pipe.Cmds)
                    {
                        List<GoNode> args = new List<GoNode>();
                        foreach (GoNode arg in command.Args)
                        {
                            GoNode walked = Walk(arg);
                            if (walked != null)
                            {
                                args.Add(walked);
                            }
                        }

                        if (args.Count == 0)
                        {
                            return null;
                        }

                        command.Args = args;
                        commands.Add(command);
                    }

                    if (commands.Count == 0)
                    {
                        return null;
                    }

                    pipe.Cmds = commands;
                    return pipe;
                }

                default:
                    return node;
            }
        }

        return (GoListNode)Walk(root);
    }
}
