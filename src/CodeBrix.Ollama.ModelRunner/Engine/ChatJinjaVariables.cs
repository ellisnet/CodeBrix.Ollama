using System;
using System.Collections.Generic;
using System.Text.Json;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Builds the variables a Jinja chat template expects out of a <see cref="ChatRequest"/>: the shape model
/// authors write their templates against, which is the shape the Hugging Face transformers library passes.
/// </summary>
/// <remarks>
/// <para>
/// The shape is documented on <see cref="JinjaTemplate"/> and is followed here to the letter, because a
/// chat template reads whatever it likes out of it: <c>messages</c> is a list of mappings carrying
/// <c>role</c> and <c>content</c> and, where the message has them, <c>reasoning_content</c>,
/// <c>tool_calls</c>, <c>tool_call_id</c> and <c>name</c>; <c>tools</c> is a list of OpenAI-shaped function
/// descriptions; <c>add_generation_prompt</c> is always true, because this library renders a prompt for a
/// reply and never for a transcript; <c>bos_token</c> and <c>eos_token</c> come from the vocabulary; and
/// <c>enable_thinking</c> appears only when the request has an opinion, so that a template's own default
/// stands when it does not.
/// </para>
/// <para>
/// Mappings are insertion-ordered, because <c>| tojson</c> writes a mapping in its key order and a
/// template's rendering of a tool definition is compared against what the model was trained on. A tool
/// call's arguments and a tool's parameters are JSON text on the contract, so they are parsed back into
/// the value model here: a template that writes <c>tool_call.function.arguments|items</c> needs a mapping,
/// not a string. Text that does not parse as JSON is passed through as a string, which is what
/// transformers does with the same input.
/// </para>
/// </remarks>
internal static class ChatJinjaVariables
{
    /// <summary>The schema used for a tool that declares no parameters at all.</summary>
    private const string EmptyParameterSchema = "{\"type\":\"object\",\"properties\":{}}";

    /// <summary>Builds the variables for one request.</summary>
    /// <param name="request">The chat request.</param>
    /// <param name="bosToken">The vocabulary's beginning-of-sequence token text; <see langword="null"/> becomes empty.</param>
    /// <param name="eosToken">The vocabulary's end-of-sequence token text; <see langword="null"/> becomes empty.</param>
    /// <returns>The variables, ready for <see cref="JinjaTemplate.Render"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public static IReadOnlyDictionary<string, object> Build(ChatRequest request, string bosToken, string eosToken)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        OrderedDictionary<string, object> variables =
            new OrderedDictionary<string, object>(StringComparer.Ordinal);

        variables["messages"] = BuildMessages(request.Messages);

        if (request.Tools.Count > 0) variables["tools"] = BuildTools(request.Tools);

        variables["add_generation_prompt"] = true;
        variables["bos_token"] = bosToken ?? string.Empty;
        variables["eos_token"] = eosToken ?? string.Empty;

        if (request.Think.HasValue) variables["enable_thinking"] = request.Think.Value;

        return variables;
    }

    /// <summary>The name a chat template knows a role by.</summary>
    /// <param name="role">The role.</param>
    /// <returns>The lower-case name.</returns>
    public static string RoleName(ChatRole role)
    {
        switch (role)
        {
            case ChatRole.System: return "system";
            case ChatRole.User: return "user";
            case ChatRole.Assistant: return "assistant";
            case ChatRole.Tool: return "tool";
            default: return role.ToString().ToLowerInvariant();
        }
    }

    /// <summary>Turns the conversation into the list of mappings a template iterates.</summary>
    /// <param name="messages">The messages.</param>
    /// <returns>The list.</returns>
    private static List<object> BuildMessages(IList<ChatMessage> messages)
    {
        List<object> list = new List<object>(messages.Count);

        foreach (ChatMessage message in messages)
        {
            if (message == null) continue;

            OrderedDictionary<string, object> mapping =
                new OrderedDictionary<string, object>(StringComparer.Ordinal);

            mapping["role"] = RoleName(message.Role);
            mapping["content"] = message.Content ?? string.Empty;

            if (!string.IsNullOrEmpty(message.Thinking)) mapping["reasoning_content"] = message.Thinking;

            if (message.ToolCalls != null && message.ToolCalls.Count > 0)
            {
                mapping["tool_calls"] = BuildToolCalls(message.ToolCalls);
            }

            if (!string.IsNullOrEmpty(message.ToolCallId)) mapping["tool_call_id"] = message.ToolCallId;
            if (!string.IsNullOrEmpty(message.ToolName)) mapping["name"] = message.ToolName;

            list.Add(mapping);
        }

        return list;
    }

    /// <summary>Turns an assistant message's tool calls into the OpenAI-shaped list.</summary>
    /// <param name="calls">The calls.</param>
    /// <returns>The list.</returns>
    private static List<object> BuildToolCalls(IList<ToolCall> calls)
    {
        List<object> list = new List<object>(calls.Count);

        foreach (ToolCall call in calls)
        {
            if (call == null) continue;

            OrderedDictionary<string, object> function =
                new OrderedDictionary<string, object>(StringComparer.Ordinal);

            function["name"] = call.Name ?? string.Empty;
            function["arguments"] = ArgumentsValue(call.ArgumentsJson);

            OrderedDictionary<string, object> entry =
                new OrderedDictionary<string, object>(StringComparer.Ordinal);

            entry["type"] = "function";
            if (!string.IsNullOrEmpty(call.Id)) entry["id"] = call.Id;
            entry["function"] = function;

            list.Add(entry);
        }

        return list;
    }

    /// <summary>Turns the tools on offer into the OpenAI-shaped list.</summary>
    /// <param name="tools">The tools.</param>
    /// <returns>The list.</returns>
    private static List<object> BuildTools(IList<ToolDefinition> tools)
    {
        List<object> list = new List<object>(tools.Count);

        foreach (ToolDefinition tool in tools)
        {
            if (tool == null) continue;

            OrderedDictionary<string, object> function =
                new OrderedDictionary<string, object>(StringComparer.Ordinal);

            function["name"] = tool.Name ?? string.Empty;
            if (!string.IsNullOrEmpty(tool.Description)) function["description"] = tool.Description;
            function["parameters"] = ParametersValue(tool.ParametersJsonSchema);

            OrderedDictionary<string, object> entry =
                new OrderedDictionary<string, object>(StringComparer.Ordinal);

            entry["type"] = "function";
            entry["function"] = function;

            list.Add(entry);
        }

        return list;
    }

    /// <summary>A tool call's arguments as a mapping, or as the original text when it is not a JSON object.</summary>
    /// <param name="argumentsJson">The arguments text.</param>
    /// <returns>The value.</returns>
    private static object ArgumentsValue(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson)) return new OrderedDictionary<string, object>(StringComparer.Ordinal);

        object parsed = TryParse(argumentsJson);
        if (parsed is OrderedDictionary<string, object>) return parsed;

        return argumentsJson;
    }

    /// <summary>A tool's parameter schema as a mapping, falling back to the empty object schema.</summary>
    /// <param name="schemaJson">The schema text.</param>
    /// <returns>The mapping.</returns>
    private static object ParametersValue(string schemaJson)
    {
        object parsed = string.IsNullOrWhiteSpace(schemaJson) ? null : TryParse(schemaJson);
        if (parsed is OrderedDictionary<string, object>) return parsed;

        return TryParse(EmptyParameterSchema);
    }

    /// <summary>Parses JSON text into the value model, or <see langword="null"/> when it is not JSON.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    private static object TryParse(string text)
    {
        try
        {
            using (JsonDocument document = JsonDocument.Parse(text))
            {
                return Convert(document.RootElement);
            }
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Converts one parsed JSON element into the Jinja value model.</summary>
    /// <param name="element">The element.</param>
    /// <returns>The value.</returns>
    private static object Convert(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                OrderedDictionary<string, object> mapping =
                    new OrderedDictionary<string, object>(StringComparer.Ordinal);

                foreach (JsonProperty property in element.EnumerateObject())
                {
                    mapping[property.Name] = Convert(property.Value);
                }

                return mapping;
            }

            case JsonValueKind.Array:
            {
                List<object> list = new List<object>();
                foreach (JsonElement item in element.EnumerateArray()) list.Add(Convert(item));
                return list;
            }

            case JsonValueKind.String:
                return element.GetString();

            case JsonValueKind.Number:
                return element.TryGetInt64(out long integer) ? integer : (object)element.GetDouble();

            case JsonValueKind.True:
                return true;

            case JsonValueKind.False:
                return false;

            default:
                return null;
        }
    }
}
