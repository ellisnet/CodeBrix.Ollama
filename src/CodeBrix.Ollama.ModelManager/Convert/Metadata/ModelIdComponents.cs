using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeBrix.Ollama.ModelManager; //was previously: gguf-py/gguf/metadata.py@b10221;

/// <summary>
/// The pieces a publisher's model identifier is taken apart into - <c>organization/Model-7B-Instruct-v0.2</c>
/// becomes an organization, a base name, a size label, a finetune and a version - and the title form of a name.
/// </summary>
/// <remarks>
/// This is the heuristic the inference engine's converter uses to fill <c>general.name</c>,
/// <c>general.basename</c>, <c>general.size_label</c> and their siblings, ported so that a conversion done here
/// carries the same general metadata as one done there. It is a heuristic over a naming convention, not a rule:
/// nothing about it is specific to any one model.
/// </remarks>
internal sealed class ModelIdComponents
{
    private static readonly Regex VersionPattern =
        new Regex(@"\A(?:(?:v|iter)?[0-9]+(?:[.][0-9]+)*)\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex QuantTypePattern =
        new Regex(@"\A(?:i?q[0-9](?:_\w)*|b?fp?(?:16|32))\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex SizePattern =
        new Regex(@"\A(?:(?:(?:[A]|[0-9]+[x])?[0-9]+(?:[._][0-9]+)?[KMBT][0-9]?)|small|mini|medium|large|x?xl)\z",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex FinetunePattern =
        new Regex(@"\A(?:chat|instruct|vision|lora)\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex TitleExemptPattern =
        new Regex(@"\A(?:v[0-9]+(?:\.[0-9]+)*|[0-9].*)\z", RegexOptions.CultureInvariant);

    private ModelIdComponents(string fullName, string organization, string basename, string finetune,
        string version, string sizeLabel)
    {
        FullName = fullName;
        Organization = organization;
        Basename = basename;
        Finetune = finetune;
        Version = version;
        SizeLabel = sizeLabel;
    }

    /// <summary>The model's own name, with the organization removed.</summary>
    internal string FullName { get; }

    /// <summary>The organization the identifier named, or <see langword="null"/>.</summary>
    internal string Organization { get; }

    /// <summary>The base name the identifier starts with, or <see langword="null"/> when it is too ambiguous.</summary>
    internal string Basename { get; }

    /// <summary>The finetune words the identifier carries, or <see langword="null"/>.</summary>
    internal string Finetune { get; }

    /// <summary>The version the identifier carries, or <see langword="null"/>.</summary>
    internal string Version { get; }

    /// <summary>The size label the identifier carries, or <see langword="null"/>.</summary>
    internal string SizeLabel { get; }

    /// <summary>Takes a model identifier apart.</summary>
    /// <param name="modelId">The identifier, for example <c>organization/model-7b-instruct</c>.</param>
    /// <param name="totalParameters">
    /// The parameter count of the model being converted, used to tell a size label from a context length. Zero
    /// when it is not known yet.
    /// </param>
    /// <returns>The components, or <see langword="null"/> when there is no identifier.</returns>
    internal static ModelIdComponents Parse(string modelId, long totalParameters)
    {
        if (modelId == null)
        {
            return null;
        }

        if (modelId.Contains(' '))
        {
            // A human sentence rather than an identifier: it is the whole name and nothing else.
            return new ModelIdComponents(modelId, null, null, null, null, null);
        }

        string organization;
        string fullName;
        int slash = modelId.IndexOf('/');
        if (slash >= 0)
        {
            organization = modelId.Substring(0, slash);
            fullName = modelId.Substring(slash + 1);
        }
        else
        {
            organization = null;
            fullName = modelId;
        }

        if (organization != null && organization.Length > 0 && organization[0] == '.')
        {
            // './' or '../' was matched rather than an organization.
            organization = null;
        }

        var parts = new List<string>(fullName.Split('-'));
        for (int i = parts.Count - 1; i >= 0; i--)
        {
            if (parts[i].Length == 0)
            {
                parts.RemoveAt(i);
            }
        }

        var types = new List<HashSet<string>>();
        for (int i = 0; i < parts.Count; i++)
        {
            types.Add(new HashSet<string>(StringComparer.Ordinal));
        }

        for (int i = 0; i < parts.Count; i++)
        {
            string part = parts[i];
            if (VersionPattern.IsMatch(part))
            {
                types[i].Add("version");
            }
            else if (QuantTypePattern.IsMatch(part))
            {
                types[i].Add("type");
                parts[i] = part.ToUpperInvariant();
            }
            else if (i > 0 && SizePattern.IsMatch(part))
            {
                AnnotateSize(parts, types, i, totalParameters);
            }
            else if (i > 0 && FinetunePattern.IsMatch(part))
            {
                if (totalParameters < 0 && string.Equals(part.ToLowerInvariant(), "lora", StringComparison.Ordinal))
                {
                    types[i].Add("type");
                }
                else
                {
                    types[i].Add("finetune");
                }
            }
        }

        DropWordSizeLabels(parts, types);
        AnnotateBasename(parts, types);

        string basename = Join(parts, types, "basename");
        string sizeLabel = JoinDistinct(parts, types, "size_label");
        string finetune = Join(parts, types, "finetune");
        string version = JoinVersionOutsideBasename(parts, types);

        if (sizeLabel == null && finetune == null && version == null)
        {
            // Too ambiguous to be worth a base name.
            basename = null;
        }

        return new ModelIdComponents(fullName, organization, basename, finetune, version, sizeLabel);
    }

    /// <summary>Turns an identifier into its title form, leaving acronyms and version numbers alone.</summary>
    /// <param name="value">The identifier.</param>
    /// <returns>The title form.</returns>
    internal static string IdToTitle(string value)
    {
        if (value == null)
        {
            return null;
        }

        string[] words = value.Trim().Replace('-', ' ')
            .Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        var builder = new StringBuilder();
        for (int i = 0; i < words.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            string word = words[i];
            builder.Append(IsLower(word) && !TitleExemptPattern.IsMatch(word) ? ToTitle(word) : word);
        }

        return builder.ToString();
    }

    /// <summary>
    /// The size label a parameter count is written as when the identifier does not carry one - <c>123K</c>,
    /// <c>7B</c>, <c>1.97B</c>.
    /// </summary>
    /// <param name="totalParameters">The model's parameter count.</param>
    /// <returns>The label.</returns>
    internal static string SizeLabelFromParameterCount(long totalParameters)
    {
        double count = Math.Abs((double)totalParameters);
        double scaled;
        string suffix;
        if (count > 1e12)
        {
            scaled = count * 1e-12;
            suffix = "T";
        }
        else if (count > 1e9)
        {
            scaled = count * 1e-9;
            suffix = "B";
        }
        else if (count > 1e6)
        {
            scaled = count * 1e-6;
            suffix = "M";
        }
        else
        {
            scaled = count * 1e-3;
            suffix = "K";
        }

        string rounded = Math.Round(scaled, MidpointRounding.ToEven)
            .ToString("F0", CultureInfo.InvariantCulture).TrimStart('0');
        int digits = Math.Max(2 - rounded.Length, 0);
        return Math.Round(scaled, digits, MidpointRounding.ToEven)
            .ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) + suffix;
    }

    private static void AnnotateSize(List<string> parts, List<HashSet<string>> types, int index,
        long totalParameters)
    {
        string part = parts[index].Replace("_", ".");
        if (char.IsDigit(part[part.Length - 1]))
        {
            // The bloom-7b1 spelling: the trailing digit belongs after the decimal point.
            part = part.Substring(0, part.Length - 2) + "." + part[part.Length - 1] + part[part.Length - 2];
        }

        if (part.Length > 1 && char.IsDigit(part[part.Length - 2]))
        {
            char last = part[part.Length - 1];
            if (last == 'k' || last == 'm' || last == 'b' || last == 't')
            {
                part = part.Substring(0, part.Length - 1) + char.ToUpperInvariant(last);
            }
        }

        if (totalParameters != 0)
        {
            double labelParameters;
            if (double.TryParse(part.Substring(0, part.Length - 1), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out labelParameters))
            {
                int scale = " KMBT".IndexOf(part[part.Length - 1]);
                labelParameters *= Math.Pow(1000, scale);
                bool looksLikeContextLength =
                    (totalParameters < 0 && labelParameters < Math.Abs(totalParameters) / 8)
                    || (totalParameters > 0
                        && Math.Abs(labelParameters - totalParameters) > 7 * totalParameters / 8);
                if (looksLikeContextLength)
                {
                    types[index].Add("finetune");
                    part = part.Substring(0, part.Length - 1)
                        + char.ToLowerInvariant(part[part.Length - 1]);
                }
            }
        }

        if (types[index].Count == 0)
        {
            types[index].Add("size_label");
        }

        parts[index] = part;
    }

    private static void DropWordSizeLabels(List<string> parts, List<HashSet<string>> types)
    {
        bool anyNumeric = false;
        for (int i = 0; i < parts.Count; i++)
        {
            if (!types[i].Contains("size_label"))
            {
                continue;
            }

            foreach (char character in parts[i])
            {
                if (char.IsDigit(character))
                {
                    anyNumeric = true;
                    break;
                }
            }
        }

        if (!anyNumeric)
        {
            return;
        }

        for (int i = 0; i < parts.Count; i++)
        {
            if (!types[i].Contains("size_label"))
            {
                continue;
            }

            bool allLetters = parts[i].Length > 0;
            foreach (char character in parts[i])
            {
                if (!char.IsLetter(character))
                {
                    allLetters = false;
                    break;
                }
            }

            if (allLetters)
            {
                types[i].Remove("size_label");
            }
        }
    }

    private static void AnnotateBasename(List<string> parts, List<HashSet<string>> types)
    {
        bool atStart = true;
        for (int i = 0; i < parts.Count; i++)
        {
            if (atStart && ((types[i].Count == 0 && char.IsLetter(parts[i][0])) || types[i].Contains("version")))
            {
                types[i].Add("basename");
            }
            else
            {
                atStart = false;
                if (types[i].Count == 0)
                {
                    types[i].Add("finetune");
                }
            }
        }

        for (int i = parts.Count - 1; i >= 0; i--)
        {
            if (types[i].Contains("basename") && types[i].Count > 1)
            {
                types[i].Remove("basename");
            }
            else
            {
                break;
            }
        }
    }

    private static string Join(List<string> parts, List<HashSet<string>> types, string kind)
    {
        var selected = new List<string>();
        for (int i = 0; i < parts.Count; i++)
        {
            if (types[i].Contains(kind))
            {
                selected.Add(parts[i]);
            }
        }

        return selected.Count == 0 ? null : string.Join("-", selected);
    }

    private static string JoinDistinct(List<string> parts, List<HashSet<string>> types, string kind)
    {
        var selected = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < parts.Count; i++)
        {
            if (types[i].Contains(kind) && seen.Add(parts[i]))
            {
                selected.Add(parts[i]);
            }
        }

        return selected.Count == 0 ? null : string.Join("-", selected);
    }

    private static string JoinVersionOutsideBasename(List<string> parts, List<HashSet<string>> types)
    {
        var selected = new List<string>();
        for (int i = 0; i < parts.Count; i++)
        {
            if (types[i].Contains("version") && !types[i].Contains("basename"))
            {
                selected.Add(parts[i]);
            }
        }

        return selected.Count == 0 ? null : string.Join("-", selected);
    }

    private static bool IsLower(string value)
    {
        bool cased = false;
        foreach (char character in value)
        {
            if (char.IsUpper(character))
            {
                return false;
            }

            if (char.IsLower(character))
            {
                cased = true;
            }
        }

        return cased;
    }

    private static string ToTitle(string value)
    {
        var builder = new StringBuilder(value.Length);
        bool previousWasCased = false;
        foreach (char character in value)
        {
            bool isCased = char.IsLower(character) || char.IsUpper(character);
            if (isCased && !previousWasCased)
            {
                builder.Append(char.ToUpperInvariant(character));
            }
            else if (isCased)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else
            {
                builder.Append(character);
            }

            previousWasCased = isCased;
        }

        return builder.ToString();
    }
}
