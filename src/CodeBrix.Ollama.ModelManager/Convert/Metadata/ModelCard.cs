using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The YAML front matter at the top of a checkpoint's <c>README.md</c> - the model card - read far enough to
/// carry the publisher's licence, tags and languages into the converted model and no further.
/// </summary>
/// <remarks>
/// <para>
/// This library has no YAML library and is not getting one, so what is read here is the subset model cards
/// actually use: a scalar (quoted or plain), a flow list <c>[a, b]</c>, and a block list of <c>- item</c> lines.
/// Comments and blank lines are skipped, and a tab is read as two spaces, which is what the inference engine's
/// converter does to the same text before it parses it.
/// </para>
/// <para>
/// Anything outside that subset - a nested mapping, a multi-line scalar, an anchor - makes THAT KEY be skipped.
/// It never fails a conversion: a model card is documentation, and a conversion that could not read one is
/// still a correct conversion of the weights.
/// </para>
/// </remarks>
internal sealed class ModelCard
{
    private readonly Dictionary<string, object> _entries;

    private ModelCard(Dictionary<string, object> entries)
    {
        _entries = entries;
    }

    /// <summary>A card with no entries, for a checkpoint that ships no <c>README.md</c>.</summary>
    internal static ModelCard Empty
    {
        get { return new ModelCard(new Dictionary<string, object>(StringComparer.Ordinal)); }
    }

    /// <summary>The keys that were read. A key outside the subset is not among them.</summary>
    internal IReadOnlyCollection<string> Keys
    {
        get { return _entries.Keys; }
    }

    /// <summary>Reads the model card in a checkpoint directory, if there is one.</summary>
    /// <param name="directory">The checkpoint directory.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The card, empty when the directory has no readable front matter.</returns>
    internal static async Task<ModelCard> LoadAsync(string directory, CancellationToken cancellationToken)
    {
        string path = Path.Combine(directory, "README.md");
        if (!File.Exists(path))
        {
            return Empty;
        }

        string content = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken)
            .ConfigureAwait(false);
        return Parse(content);
    }

    /// <summary>Reads front matter out of the text of a model card.</summary>
    /// <param name="content">The whole file.</param>
    /// <returns>The card.</returns>
    internal static ModelCard Parse(string content)
    {
        var entries = new Dictionary<string, object>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(content))
        {
            return new ModelCard(entries);
        }

        string[] lines = content.Replace("\t", "  ").Split('\n');
        if (lines.Length == 0 || TrimLineEnd(lines[0]) != "---")
        {
            return new ModelCard(entries);
        }

        var front = new List<string>();
        for (int i = 1; i < lines.Length; i++)
        {
            string line = TrimLineEnd(lines[i]);
            if (line == "---")
            {
                break;
            }

            front.Add(line);
        }

        ParseFrontMatter(front, entries);
        return new ModelCard(entries);
    }

    /// <summary>Gets a scalar entry.</summary>
    /// <param name="key">The front-matter key.</param>
    /// <param name="value">Receives the value when the key holds a scalar.</param>
    /// <returns><see langword="true"/> when the key holds a scalar.</returns>
    internal bool TryGetString(string key, out string value)
    {
        if (_entries.TryGetValue(key, out object entry) && entry is string text)
        {
            value = text;
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>Gets a list entry, or a scalar entry as a list of one.</summary>
    /// <param name="key">The front-matter key.</param>
    /// <param name="values">Receives the values when the key holds a scalar or a list.</param>
    /// <returns><see langword="true"/> when the key holds a scalar or a list.</returns>
    internal bool TryGetStrings(string key, out IReadOnlyList<string> values)
    {
        if (_entries.TryGetValue(key, out object entry))
        {
            if (entry is List<string> list)
            {
                values = list;
                return true;
            }

            if (entry is string text)
            {
                values = new List<string> { text };
                return true;
            }
        }

        values = null;
        return false;
    }

    private static void ParseFrontMatter(List<string> lines, Dictionary<string, object> entries)
    {
        int index = 0;
        while (index < lines.Count)
        {
            string line = lines[index];
            index++;
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                continue;
            }

            if (line.Length > 0 && char.IsWhiteSpace(line[0]))
            {
                // An indented line that no key claimed: part of a construct outside the subset.
                continue;
            }

            int colon = FindKeyColon(line);
            if (colon < 0)
            {
                continue;
            }

            string key = line.Substring(0, colon).Trim();
            string rest = line.Substring(colon + 1).Trim();
            if (key.Length == 0)
            {
                continue;
            }

            if (rest.Length == 0)
            {
                List<string> block = ReadBlockList(lines, ref index);
                if (block != null)
                {
                    entries[key] = block;
                }

                continue;
            }

            if (rest[0] == '#')
            {
                continue;
            }

            if (rest[0] == '[')
            {
                List<string> flow = ReadFlowList(rest);
                if (flow != null)
                {
                    entries[key] = flow;
                }

                continue;
            }

            string scalar = ReadScalar(rest);
            if (scalar != null)
            {
                entries[key] = scalar;
            }
        }
    }

    private static List<string> ReadBlockList(List<string> lines, ref int index)
    {
        var items = new List<string>();
        bool readable = true;
        while (index < lines.Count)
        {
            string line = lines[index];
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                index++;
                continue;
            }

            if (line.Length > 0 && !char.IsWhiteSpace(line[0]) && trimmed[0] != '-')
            {
                break;
            }

            if (trimmed[0] != '-')
            {
                // An indented line that is not a list item: a nested mapping, which is outside the subset.
                readable = false;
                index++;
                continue;
            }

            index++;
            string item = ReadScalar(trimmed.Substring(1).Trim());
            if (item == null)
            {
                readable = false;
                continue;
            }

            items.Add(item);
        }

        return readable && items.Count > 0 ? items : null;
    }

    private static List<string> ReadFlowList(string text)
    {
        int end = text.LastIndexOf(']');
        if (end < 0)
        {
            return null;
        }

        string inner = text.Substring(1, end - 1);
        var items = new List<string>();
        if (inner.Trim().Length == 0)
        {
            return items;
        }

        foreach (string piece in inner.Split(','))
        {
            string item = ReadScalar(piece.Trim());
            if (item == null)
            {
                return null;
            }

            items.Add(item);
        }

        return items;
    }

    private static string ReadScalar(string text)
    {
        if (text.Length == 0)
        {
            return null;
        }

        char quote = text[0];
        if (quote == '"' || quote == '\'')
        {
            int end = text.IndexOf(quote, 1);
            if (end < 0)
            {
                return null;
            }

            return text.Substring(1, end - 1);
        }

        if (text[0] == '{' || text[0] == '[' || text[0] == '&' || text[0] == '*' || text[0] == '|'
            || text[0] == '>')
        {
            // Flow mappings, anchors, aliases and block scalars are outside the subset.
            return null;
        }

        int comment = text.IndexOf(" #", StringComparison.Ordinal);
        string value = comment >= 0 ? text.Substring(0, comment) : text;
        value = value.Trim();
        return value.Length == 0 ? null : value;
    }

    private static int FindKeyColon(string line)
    {
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == ':' && (i + 1 == line.Length || line[i + 1] == ' '))
            {
                return i;
            }
        }

        return -1;
    }

    private static string TrimLineEnd(string line)
    {
        return line.EndsWith("\r", StringComparison.Ordinal) ? line.Substring(0, line.Length - 1) : line;
    }
}
