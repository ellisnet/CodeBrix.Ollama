using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager; //was previously: conversion/base.py@b10221;

/// <summary>
/// A checkpoint's <c>config.json</c>, read as the converter sees it.
/// </summary>
/// <remarks>
/// <para>
/// The inference engine's converter does not read <c>config.json</c> directly: it loads the file through the
/// transformers library, which fills in the fields the model class defaults. Two of those defaults reach the
/// GGUF file, so they are applied here too when the configuration says <c>llama</c>: <c>head_dim</c>, which the
/// class derives from the hidden size and the head count, and <c>num_key_value_heads</c>, which falls back to
/// the attention head count. Everything else is read exactly as the file wrote it.
/// </para>
/// <para>
/// Lookups take a list of names and answer with the first one the file holds, which is how the converter reads
/// a hyper-parameter that different publishers spell differently.
/// </para>
/// </remarks>
internal sealed class HuggingFaceConfig
{
    private readonly Dictionary<string, object> _values;

    private HuggingFaceConfig(Dictionary<string, object> values)
    {
        _values = values;
    }

    /// <summary>The architecture names the file lists, empty when it lists none.</summary>
    internal IReadOnlyList<string> Architectures
    {
        get
        {
            var result = new List<string>();
            if (_values.TryGetValue("architectures", out object value) && value is List<object> list)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] is string name)
                    {
                        result.Add(name);
                    }
                }
            }

            return result;
        }
    }

    /// <summary>The value of <c>model_type</c>, or <see langword="null"/>.</summary>
    internal string ModelType
    {
        get { return GetString("model_type"); }
    }

    /// <summary>Reads the <c>config.json</c> in a checkpoint directory.</summary>
    /// <param name="directory">The checkpoint directory.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The configuration.</returns>
    internal static async Task<HuggingFaceConfig> LoadAsync(string directory, CancellationToken cancellationToken)
    {
        string path = Path.Combine(directory, "config.json");
        if (!File.Exists(path))
        {
            throw new CheckpointFormatException("The folder \"" + directory + "\" holds no config.json, so it " +
                "is not a checkpoint this library can convert.");
        }

        byte[] content = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return Parse(content, path);
    }

    /// <summary>Reads a configuration from the bytes of a <c>config.json</c>.</summary>
    /// <param name="content">The file's bytes.</param>
    /// <param name="path">The path, for messages.</param>
    /// <returns>The configuration.</returns>
    internal static HuggingFaceConfig Parse(byte[] content, string path)
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        try
        {
            using (JsonDocument document = JsonDocument.Parse(content))
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new CheckpointFormatException("The file \"" + path + "\" is not a JSON object.");
                }

                foreach (JsonProperty property in document.RootElement.EnumerateObject())
                {
                    values[property.Name] = Convert(property.Value);
                }
            }
        }
        catch (JsonException exception)
        {
            throw new CheckpointFormatException("The file \"" + path + "\" is not valid JSON.", exception);
        }

        var config = new HuggingFaceConfig(values);
        config.ApplyTransformersDefaults();
        return config;
    }

    /// <summary>Gets a string value.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    internal string GetString(string key)
    {
        return _values.TryGetValue(key, out object value) ? value as string : null;
    }

    /// <summary>Gets the first of several keys that holds a whole number.</summary>
    /// <param name="keys">The names to try, in order.</param>
    /// <param name="value">Receives the value.</param>
    /// <returns><see langword="true"/> when one of the keys holds a whole number.</returns>
    internal bool TryGetInt64(IReadOnlyList<string> keys, out long value)
    {
        for (int i = 0; i < keys.Count; i++)
        {
            if (!_values.TryGetValue(keys[i], out object entry))
            {
                continue;
            }

            if (entry is long number)
            {
                value = number;
                return true;
            }

            if (entry is double real && real == Math.Floor(real))
            {
                value = (long)real;
                return true;
            }

            break;
        }

        value = 0;
        return false;
    }

    /// <summary>Gets the first of several keys that holds a number.</summary>
    /// <param name="keys">The names to try, in order.</param>
    /// <param name="value">Receives the value.</param>
    /// <returns><see langword="true"/> when one of the keys holds a number.</returns>
    internal bool TryGetDouble(IReadOnlyList<string> keys, out double value)
    {
        for (int i = 0; i < keys.Count; i++)
        {
            if (!_values.TryGetValue(keys[i], out object entry))
            {
                continue;
            }

            if (entry is double real)
            {
                value = real;
                return true;
            }

            if (entry is long number)
            {
                value = number;
                return true;
            }

            break;
        }

        value = 0;
        return false;
    }

    /// <summary>Gets a boolean value.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">Receives the value.</param>
    /// <returns><see langword="true"/> when the key holds a boolean.</returns>
    internal bool TryGetBoolean(string key, out bool value)
    {
        if (_values.TryGetValue(key, out object entry) && entry is bool flag)
        {
            value = flag;
            return true;
        }

        value = false;
        return false;
    }

    /// <summary>Whether the file holds a key at all, whatever its value.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> when the key is present.</returns>
    internal bool Contains(string key)
    {
        return _values.ContainsKey(key);
    }

    /// <summary>Gets a number nested under another key, for example a rope parameter.</summary>
    /// <param name="parent">The key of the nested object.</param>
    /// <param name="key">The key inside it.</param>
    /// <param name="value">Receives the value.</param>
    /// <returns><see langword="true"/> when both keys are present and the value is a number.</returns>
    internal bool TryGetNestedDouble(string parent, string key, out double value)
    {
        if (_values.TryGetValue(parent, out object entry) && entry is Dictionary<string, object> nested
            && nested.TryGetValue(key, out object inner))
        {
            if (inner is double real)
            {
                value = real;
                return true;
            }

            if (inner is long number)
            {
                value = number;
                return true;
            }
        }

        value = 0;
        return false;
    }

    /// <summary>Gets a string nested under another key.</summary>
    /// <param name="parent">The key of the nested object.</param>
    /// <param name="key">The key inside it.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    internal string GetNestedString(string parent, string key)
    {
        if (_values.TryGetValue(parent, out object entry) && entry is Dictionary<string, object> nested
            && nested.TryGetValue(key, out object inner))
        {
            return inner as string;
        }

        return null;
    }

    private void ApplyTransformersDefaults()
    {
        if (!string.Equals(ModelType, "llama", StringComparison.Ordinal))
        {
            return;
        }

        if (!_values.ContainsKey("num_key_value_heads")
            && _values.TryGetValue("num_attention_heads", out object heads))
        {
            _values["num_key_value_heads"] = heads;
        }

        if (!_values.ContainsKey("head_dim")
            && TryGetInt64(new[] { "hidden_size" }, out long hiddenSize)
            && TryGetInt64(new[] { "num_attention_heads" }, out long headCount)
            && headCount > 0)
        {
            _values["head_dim"] = hiddenSize / headCount;
        }
    }

    private static object Convert(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Number:
                if (element.TryGetInt64(out long number))
                {
                    return number;
                }

                return element.GetDouble();
            case JsonValueKind.Array:
                var list = new List<object>();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    list.Add(Convert(item));
                }

                return list;
            case JsonValueKind.Object:
                var nested = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    nested[property.Name] = Convert(property.Value);
                }

                return nested;
            default:
                return null;
        }
    }
}
