using System.Collections.Generic;
using System.Text.Json;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ollama/ollama template/template.go and api/types.go;

/// <summary>
/// Turns this library's chat types into the shapes Ollama hands to a template, field name for field
/// name and JSON name for JSON name, so a template written for Ollama sees exactly what it expects.
/// </summary>
internal static class OllamaTemplateBinder
{
    /// <summary>The Go type name of a tool property, which <c>toTypeScriptType</c> recognizes.</summary>
    internal const string ToolPropertyTypeName = "api.ToolProperty";

    /// <summary>
    /// Names a role the way Ollama's API spells it.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <returns>The role name.</returns>
    internal static string RoleName(ChatRole role)
    {
        switch (role)
        {
            case ChatRole.System:
                return "system";
            case ChatRole.Assistant:
                return "assistant";
            case ChatRole.Tool:
                return "tool";
            default:
                return "user";
        }
    }

    /// <summary>
    /// Converts collated messages to the slice of message structs a template ranges over.
    /// </summary>
    /// <param name="messages">The collated messages.</param>
    /// <returns>The slice, which is a nil slice when there are no messages.</returns>
    internal static GoSlice Messages(IReadOnlyList<ChatMessage> messages)
    {
        GoSlice slice = new GoSlice();
        if (messages == null || messages.Count == 0)
        {
            slice.IsNil = true;
            return slice;
        }

        foreach (ChatMessage message in messages)
        {
            slice.Items.Add(Message(message));
        }

        return slice;
    }

    /// <summary>
    /// Converts a message to the struct a template reads its fields from.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>The struct.</returns>
    internal static GoStruct Message(ChatMessage message)
    {
        GoStruct structure = new GoStruct("template.templateMessage");
        structure.Add("Role", "Role", false, RoleName(message.Role));
        structure.Add("Content", "Content", false, message.Content ?? string.Empty);
        structure.Add("Thinking", "Thinking", false, message.Thinking ?? string.Empty);
        structure.Add("Images", "Images", false, new GoSlice { IsNil = true });
        structure.Add("ToolCalls", "ToolCalls", false, ToolCalls(message.ToolCalls));
        structure.Add("ToolName", "ToolName", false, message.ToolName ?? string.Empty);
        structure.Add("ToolCallID", "ToolCallID", false, message.ToolCallId ?? string.Empty);
        return structure;
    }

    /// <summary>
    /// Converts tool definitions to the slice of tool structs a template ranges over. Printing the slice
    /// yields the JSON Ollama's <c>templateTools</c> prints.
    /// </summary>
    /// <param name="tools">The tools offered with the request.</param>
    /// <returns>The slice, which is a nil slice when there are no tools.</returns>
    internal static GoSlice Tools(IReadOnlyList<ToolDefinition> tools)
    {
        GoSlice slice = new GoSlice { Stringer = s => GoJson.Marshal(s) };
        if (tools == null || tools.Count == 0)
        {
            slice.IsNil = true;
            return slice;
        }

        foreach (ToolDefinition tool in tools)
        {
            slice.Items.Add(Tool(tool));
        }

        return slice;
    }

    /// <summary>
    /// Parses JSON text into the untyped value shapes a template works with.
    /// </summary>
    /// <param name="json">The JSON text. <see langword="null"/>, empty or malformed text yields
    /// <see langword="null"/>.</param>
    /// <returns>The parsed value.</returns>
    internal static object ParseJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                return FromJson(document.RootElement);
            }
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static GoSlice ToolCalls(IList<ToolCall> calls)
    {
        GoSlice slice = new GoSlice();
        if (calls == null || calls.Count == 0)
        {
            slice.IsNil = true;
            return slice;
        }

        for (int i = 0; i < calls.Count; i++)
        {
            // A caller's list can hold a null, the same way the whole list can be null; neither is a call,
            // and a template that ranges over them must not meet one.
            ToolCall call = calls[i];
            if (call == null) continue;

            GoStruct function = new GoStruct("template.templateToolCallFunction");
            function.Add("Index", "Index", false, (long)i);
            function.Add("Name", "Name", false, call.Name ?? string.Empty);
            function.Add("Arguments", "Arguments", false, Arguments(call.ArgumentsJson));

            GoStruct structure = new GoStruct("template.templateToolCall");
            structure.Add("ID", "ID", false, call.Id ?? string.Empty);
            structure.Add("Function", "Function", false, function);
            slice.Items.Add(structure);
        }

        if (slice.Items.Count == 0) slice.IsNil = true;

        return slice;
    }

    private static GoMap Arguments(string argumentsJson)
    {
        GoMap map = new GoMap { Stringer = m => m.IsNil ? "{}" : GoJson.Marshal(m) };
        object parsed = ParseJson(argumentsJson);
        if (!(parsed is GoMap source))
        {
            map.IsNil = true;
            return map;
        }

        foreach (KeyValuePair<string, object> entry in source.Entries)
        {
            map.Set(entry.Key, entry.Value);
        }

        return map;
    }

    private static GoStruct Tool(ToolDefinition tool)
    {
        object schema = ParseJson(tool?.ParametersJsonSchema);
        GoMap schemaMap = schema as GoMap;

        GoStruct parameters = new GoStruct("template.templateToolFunctionParameters");
        parameters.Add("Type", "type", false, Lookup(schemaMap, "type") as string ?? string.Empty);
        parameters.Add("Defs", "$defs", true, Lookup(schemaMap, "$defs"));
        parameters.Add("Items", "items", true, Lookup(schemaMap, "items"));
        parameters.Add("Required", "required", true, StringSlice(Lookup(schemaMap, "required")));
        parameters.Add("Properties", "properties", false, Properties(Lookup(schemaMap, "properties") as GoMap));

        GoStruct function = new GoStruct("template.templateToolFunction");
        function.Add("Name", "name", false, tool?.Name ?? string.Empty);
        function.Add("Description", "description", false, tool?.Description ?? string.Empty);
        function.Add("Parameters", "parameters", false, parameters);

        GoStruct structure = new GoStruct("template.templateTool");
        structure.Add("Type", "type", false, "function");
        structure.Add("Items", "items", true, null);
        structure.Add("Function", "function", false, function);
        structure.Stringer = s => GoJson.Marshal(s);
        return structure;
    }

    private static GoMap Properties(GoMap source)
    {
        GoMap map = new GoMap { Stringer = m => m.IsNil ? "{}" : GoJson.Marshal(m) };
        if (source == null)
        {
            map.IsNil = true;
            map.MissingValue = ToolProperty(null);
            return map;
        }

        map.MissingValue = ToolProperty(null);
        foreach (KeyValuePair<string, object> entry in source.Entries)
        {
            map.Set(entry.Key, ToolProperty(entry.Value as GoMap));
        }

        return map;
    }

    private static GoStruct ToolProperty(GoMap source)
    {
        GoStruct structure = new GoStruct(ToolPropertyTypeName);
        structure.Add("AnyOf", "anyOf", true, AnyOf(Lookup(source, "anyOf")));
        structure.Add("Type", "type", true, PropertyType(Lookup(source, "type")));
        structure.Add("Items", "items", true, Lookup(source, "items"));
        structure.Add("Description", "description", true, Lookup(source, "description") as string ?? string.Empty);
        structure.Add("Enum", "enum", true, GenericSlice(Lookup(source, "enum")));
        structure.Add("Properties", "properties", true, NestedProperties(Lookup(source, "properties") as GoMap));
        structure.Add("Required", "required", true, StringSlice(Lookup(source, "required")));
        return structure;
    }

    private static object NestedProperties(GoMap source) => source == null ? null : Properties(source);

    private static GoSlice AnyOf(object value)
    {
        GoSlice slice = new GoSlice();
        if (!(value is GoSlice source) || source.Items.Count == 0)
        {
            slice.IsNil = true;
            return slice;
        }

        foreach (object item in source.Items)
        {
            slice.Items.Add(ToolProperty(item as GoMap));
        }

        return slice;
    }

    private static GoSlice PropertyType(object value)
    {
        //api.PropertyType is a string slice that reads and writes as a bare string when it holds one type
        GoSlice slice = new GoSlice
        {
            Stringer = s => s.Items.Count == 0 ? string.Empty
                : s.Items.Count == 1 ? s.Items[0] as string ?? string.Empty
                : GoFormat.Print(new GoSlice(s.Items)),
            JsonMarshaler = s => s.Items.Count == 1
                ? GoJson.Marshal(s.Items[0])
                : GoJson.Marshal(new GoSlice(s.Items)),
        };
        switch (value)
        {
            case string single:
                slice.Items.Add(single);
                return slice;
            case GoSlice many:
                foreach (object item in many.Items)
                {
                    slice.Items.Add(item as string ?? GoFormat.Print(item));
                }

                return slice;
            default:
                slice.IsNil = true;
                return slice;
        }
    }

    private static GoSlice StringSlice(object value)
    {
        GoSlice slice = new GoSlice();
        if (!(value is GoSlice source) || source.Items.Count == 0)
        {
            slice.IsNil = true;
            return slice;
        }

        foreach (object item in source.Items)
        {
            slice.Items.Add(item as string ?? GoFormat.Print(item));
        }

        return slice;
    }

    private static GoSlice GenericSlice(object value)
    {
        GoSlice slice = new GoSlice();
        if (!(value is GoSlice source) || source.Items.Count == 0)
        {
            slice.IsNil = true;
            return slice;
        }

        slice.Items.AddRange(source.Items);
        return slice;
    }

    private static object Lookup(GoMap map, string key)
        => map != null && map.Entries.TryGetValue(key, out object value) ? value : null;

    private static object FromJson(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                GoMap map = new GoMap();
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    map.Set(property.Name, FromJson(property.Value));
                }

                return map;
            }

            case JsonValueKind.Array:
            {
                GoSlice slice = new GoSlice();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    slice.Items.Add(FromJson(item));
                }

                return slice;
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
