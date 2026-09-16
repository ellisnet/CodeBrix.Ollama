using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp common/json-schema-to-grammar.cpp;

/// <summary>
/// Turns a JSON schema into the GBNF grammar that constrains a model to produce JSON matching it.
/// </summary>
/// <remarks>
/// <para>
/// This is a port of llama.cpp's own converter, rule for rule and name for name, so a grammar produced
/// here is byte-identical to the one the C++ produces for the same schema. The rule table is kept in
/// ordinal key order, as the original's <c>std::map</c> is, while an object's properties keep the order the
/// schema wrote them in, so both the rule listing and the alternatives inside a rule come out the same way.
/// </para>
/// <para>
/// <see cref="JsonSchemaGrammar"/> is the public face of this; the class itself is one conversion's
/// working state and is used once and thrown away.
/// </para>
/// </remarks>
internal sealed class JsonSchemaConverter
{
    /// <summary>The optional-whitespace rule every generated grammar carries.</summary>
    private const string SpaceRule =
        """
        | " " | "\n"{1,2} [ \t]{0,20}
        """;

    private static readonly Regex InvalidRuleCharacters = new Regex("[^a-zA-Z0-9-]+", RegexOptions.Compiled);
    private static readonly Regex UuidFormat = new Regex("^uuid[1-5]?$", RegexOptions.Compiled);

    private static readonly char[] NonLiteralCharacters =
        { '|', '.', '(', ')', '[', ']', '{', '}', '*', '+', '?' };

    private static readonly char[] EscapedInRegexpsButNotInLiterals =
        { '^', '$', '.', '[', ']', '(', ')', '|', '{', '}', '*', '+', '?' };

    private static readonly IReadOnlyDictionary<string, GrammarBuiltinRule> PrimitiveRules = BuildPrimitiveRules();

    private static readonly IReadOnlyDictionary<string, GrammarBuiltinRule> StringFormatRules = BuildStringFormatRules();

    private static readonly HashSet<string> ReservedNames = BuildReservedNames();

    private readonly SortedDictionary<string, string> _rules =
        new SortedDictionary<string, string>(StringComparer.Ordinal);

    private readonly Dictionary<string, JsonElement> _references =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    private readonly HashSet<string> _referencesBeingResolved = new HashSet<string>(StringComparer.Ordinal);

    private readonly List<string> _errors = new List<string>();

    private readonly List<string> _warnings = new List<string>();

    private readonly bool _dotAll;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonSchemaConverter"/> class.
    /// </summary>
    /// <param name="dotAll">
    /// Whether a <c>.</c> in a schema pattern should also match a line break. The C++ default, and the one
    /// used for schema conversion, is <see langword="false"/>.
    /// </param>
    public JsonSchemaConverter(bool dotAll = false)
    {
        _dotAll = dotAll;
        _rules["space"] = SpaceRule;
    }

    /// <summary>
    /// What the converter had to widen or skip while still producing a usable grammar. Empty for the
    /// schemas it converts exactly.
    /// </summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>
    /// Converts a schema into a grammar.
    /// </summary>
    /// <param name="schema">The schema.</param>
    /// <returns>The grammar text.</returns>
    /// <exception cref="GrammarException">The schema uses something the converter cannot express.</exception>
    public string Convert(JsonElement schema)
    {
        ResolveReferences(schema, schema);
        Visit(schema, string.Empty);
        CheckErrors();
        return FormatGrammar();
    }

    /// <summary>
    /// Builds the table of JSON primitive rules.
    /// </summary>
    /// <returns>The table, keyed by rule name.</returns>
    private static IReadOnlyDictionary<string, GrammarBuiltinRule> BuildPrimitiveRules()
    {
        return new Dictionary<string, GrammarBuiltinRule>(StringComparer.Ordinal)
        {
            ["boolean"] = new GrammarBuiltinRule(
                """
                ("true" | "false")
                """),
            ["decimal-part"] = new GrammarBuiltinRule("[0-9]{1,16}"),
            ["integral-part"] = new GrammarBuiltinRule("[0] | [1-9] [0-9]{0,15}"),
            ["number"] = new GrammarBuiltinRule(
                """
                ("-"? integral-part) ("." decimal-part)? ([eE] [-+]? integral-part)?
                """,
                "integral-part", "decimal-part"),
            ["integer"] = new GrammarBuiltinRule(
                """
                ("-"? integral-part)
                """,
                "integral-part"),
            ["value"] = new GrammarBuiltinRule("object | array | string | number | boolean | null",
                "object", "array", "string", "number", "boolean", "null"),
            ["object"] = new GrammarBuiltinRule(
                """
                "{" space ( string ":" space value ("," space string ":" space value)* )? space "}"
                """,
                "string", "value"),
            ["array"] = new GrammarBuiltinRule(
                """
                "[" space ( value ("," space value)* )? space "]"
                """,
                "value"),
            ["uuid"] = new GrammarBuiltinRule(
                """
                "\"" [0-9a-fA-F]{8} "-" [0-9a-fA-F]{4} "-" [0-9a-fA-F]{4} "-" [0-9a-fA-F]{4} "-" [0-9a-fA-F]{12} "\""
                """),
            ["char"] = new GrammarBuiltinRule(
                """
                [^"\\\x7F\x00-\x1F] | [\\] (["\\bfnrt] | "u" [0-9a-fA-F]{4})
                """),
            ["string"] = new GrammarBuiltinRule(
                """
                "\"" char* "\""
                """,
                "char"),
            ["null"] = new GrammarBuiltinRule(
                """
                "null"
                """),
        };
    }

    /// <summary>
    /// Builds the table of string-format rules.
    /// </summary>
    /// <returns>The table, keyed by rule name.</returns>
    private static IReadOnlyDictionary<string, GrammarBuiltinRule> BuildStringFormatRules()
    {
        return new Dictionary<string, GrammarBuiltinRule>(StringComparer.Ordinal)
        {
            ["date"] = new GrammarBuiltinRule(
                """
                [0-9]{4} "-" ( "0" [1-9] | "1" [0-2] ) "-" ( "0" [1-9] | [1-2] [0-9] | "3" [0-1] )
                """),
            ["time"] = new GrammarBuiltinRule(
                """
                ([01] [0-9] | "2" [0-3]) ":" [0-5] [0-9] ":" [0-5] [0-9] ( "." [0-9]{3} )? ( "Z" | ( "+" | "-" ) ( [01] [0-9] | "2" [0-3] ) ":" [0-5] [0-9] )
                """),
            ["date-time"] = new GrammarBuiltinRule(
                """
                date "T" time
                """,
                "date", "time"),
            ["date-string"] = new GrammarBuiltinRule(
                """
                "\"" date "\""
                """,
                "date"),
            ["time-string"] = new GrammarBuiltinRule(
                """
                "\"" time "\""
                """,
                "time"),
            ["date-time-string"] = new GrammarBuiltinRule(
                """
                "\"" date-time "\""
                """,
                "date-time"),
        };
    }

    /// <summary>
    /// Builds the set of rule names a schema may not take for itself.
    /// </summary>
    /// <returns>The reserved names.</returns>
    private static HashSet<string> BuildReservedNames()
    {
        HashSet<string> names = new HashSet<string>(StringComparer.Ordinal) { "root" };
        foreach (string name in PrimitiveRules.Keys)
        {
            names.Add(name);
        }

        foreach (string name in StringFormatRules.Keys)
        {
            names.Add(name);
        }

        return names;
    }

    /// <summary>
    /// Escapes a string so it can be written as a grammar literal, quotes included.
    /// </summary>
    /// <param name="literal">The text.</param>
    /// <returns>The quoted literal.</returns>
    private static string FormatLiteral(string literal)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append('"');
        foreach (char c in literal)
        {
            switch (c)
            {
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    /// <summary>
    /// Writes the repetition of a rule, choosing the shortest spelling GBNF allows.
    /// </summary>
    /// <param name="itemRule">The rule being repeated.</param>
    /// <param name="minItems">The smallest number of repetitions.</param>
    /// <param name="maxItems">The largest number of repetitions, or <see cref="int.MaxValue"/> for no limit.</param>
    /// <param name="separatorRule">The rule between repetitions, or an empty string when there is none.</param>
    /// <returns>The repetition expression.</returns>
    private static string BuildRepetition(string itemRule, int minItems, int maxItems,
        string separatorRule = "")
    {
        bool hasMax = maxItems != int.MaxValue;

        if (maxItems == 0)
        {
            return string.Empty;
        }

        if (minItems == 0 && maxItems == 1)
        {
            return itemRule + "?";
        }

        if (string.IsNullOrEmpty(separatorRule))
        {
            if (minItems == 1 && !hasMax)
            {
                return itemRule + "+";
            }

            if (minItems == 0 && !hasMax)
            {
                return itemRule + "*";
            }

            return itemRule + "{" + minItems.ToString(CultureInfo.InvariantCulture) + ","
                   + (hasMax ? maxItems.ToString(CultureInfo.InvariantCulture) : string.Empty) + "}";
        }

        string result = itemRule + " " + BuildRepetition(
            "(" + separatorRule + " " + itemRule + ")",
            minItems == 0 ? 0 : minItems - 1,
            hasMax ? maxItems - 1 : maxItems);
        if (minItems == 0)
        {
            result = "(" + result + ")?";
        }

        return result;
    }

    /// <summary>
    /// Writes the rule that matches the integers between two bounds.
    /// </summary>
    /// <param name="minValue">The smallest value, or <see cref="long.MinValue"/> for no lower bound.</param>
    /// <param name="maxValue">The largest value, or <see cref="long.MaxValue"/> for no upper bound.</param>
    /// <param name="output">The buffer being built.</param>
    /// <param name="decimalsLeft">How many further digits the recursion may still write.</param>
    /// <param name="topLevel">Whether this call writes the whole number rather than a tail of one.</param>
    private static void BuildMinMaxInt(long minValue, long maxValue, StringBuilder output,
        int decimalsLeft = 16, bool topLevel = true)
    {
        bool hasMin = minValue != long.MinValue;
        bool hasMax = maxValue != long.MaxValue;

        void DigitRange(char from, char to)
        {
            output.Append('[');
            if (from == to)
            {
                output.Append(from);
            }
            else
            {
                output.Append(from).Append('-').Append(to);
            }

            output.Append(']');
        }

        void MoreDigits(int minDigits, int maxDigits)
        {
            output.Append("[0-9]");
            if (minDigits == maxDigits && minDigits == 1)
            {
                return;
            }

            output.Append('{');
            output.Append(minDigits.ToString(CultureInfo.InvariantCulture));
            if (maxDigits != minDigits)
            {
                output.Append(',');
                if (maxDigits != int.MaxValue)
                {
                    output.Append(maxDigits.ToString(CultureInfo.InvariantCulture));
                }
            }

            output.Append('}');
        }

        void UniformRange(string from, string to)
        {
            int i = 0;
            while (i < from.Length && i < to.Length && from[i] == to[i])
            {
                i++;
            }

            if (i > 0)
            {
                output.Append('"').Append(from.Substring(0, i)).Append('"');
            }

            if (i < from.Length && i < to.Length)
            {
                if (i > 0)
                {
                    output.Append(' ');
                }

                int subLength = from.Length - i - 1;
                if (subLength > 0)
                {
                    string fromSub = from.Substring(i + 1);
                    string toSub = to.Substring(i + 1);
                    string subZeros = new string('0', subLength);
                    string subNines = new string('9', subLength);

                    bool toReached = false;
                    output.Append('(');
                    if (fromSub == subZeros)
                    {
                        DigitRange(from[i], (char)(to[i] - 1));
                        output.Append(' ');
                        MoreDigits(subLength, subLength);
                    }
                    else
                    {
                        output.Append('[').Append(from[i]).Append("] ");
                        output.Append('(');
                        UniformRange(fromSub, subNines);
                        output.Append(')');
                        if (from[i] < to[i] - 1)
                        {
                            output.Append(" | ");
                            if (toSub == subNines)
                            {
                                DigitRange((char)(from[i] + 1), to[i]);
                                toReached = true;
                            }
                            else
                            {
                                DigitRange((char)(from[i] + 1), (char)(to[i] - 1));
                            }

                            output.Append(' ');
                            MoreDigits(subLength, subLength);
                        }
                    }

                    if (!toReached)
                    {
                        output.Append(" | ");
                        DigitRange(to[i], to[i]);
                        output.Append(' ');
                        UniformRange(subZeros, toSub);
                    }

                    output.Append(')');
                }
                else
                {
                    output.Append('[').Append(from[i]).Append('-').Append(to[i]).Append(']');
                }
            }
        }

        if (hasMin && hasMax)
        {
            if (minValue < 0 && maxValue < 0)
            {
                output.Append("\"-\" (");
                BuildMinMaxInt(-maxValue, -minValue, output, decimalsLeft, true);
                output.Append(')');
                return;
            }

            if (minValue < 0)
            {
                output.Append("\"-\" (");
                BuildMinMaxInt(0, -minValue, output, decimalsLeft, true);
                output.Append(") | ");
                minValue = 0;
            }

            string minText = minValue.ToString(CultureInfo.InvariantCulture);
            string maxText = maxValue.ToString(CultureInfo.InvariantCulture);
            int minDigits = minText.Length;
            int maxDigits = maxText.Length;

            for (int digits = minDigits; digits < maxDigits; digits++)
            {
                UniformRange(minText, new string('9', digits));
                minText = "1" + new string('0', digits);
                output.Append(" | ");
            }

            UniformRange(minText, maxText);
            return;
        }

        int lessDecimals = Math.Max(decimalsLeft - 1, 1);

        if (hasMin)
        {
            if (minValue < 0)
            {
                output.Append("\"-\" (");
                BuildMinMaxInt(long.MinValue, -minValue, output, decimalsLeft, false);
                output.Append(") | [0] | [1-9] ");
                MoreDigits(0, decimalsLeft - 1);
            }
            else if (minValue == 0)
            {
                if (topLevel)
                {
                    output.Append("[0] | [1-9] ");
                    MoreDigits(0, lessDecimals);
                }
                else
                {
                    MoreDigits(1, decimalsLeft);
                }
            }
            else if (minValue <= 9)
            {
                char c = (char)('0' + minValue);
                char rangeStart = topLevel ? '1' : '0';
                if (c > rangeStart)
                {
                    DigitRange(rangeStart, (char)(c - 1));
                    output.Append(' ');
                    MoreDigits(1, lessDecimals);
                    output.Append(" | ");
                }

                DigitRange(c, '9');
                output.Append(' ');
                MoreDigits(0, lessDecimals);
            }
            else
            {
                string minText = minValue.ToString(CultureInfo.InvariantCulture);
                int length = minText.Length;
                char c = minText[0];

                if (c > '1')
                {
                    DigitRange(topLevel ? '1' : '0', (char)(c - 1));
                    output.Append(' ');
                    MoreDigits(length, lessDecimals);
                    output.Append(" | ");
                }

                DigitRange(c, c);
                output.Append(" (");
                BuildMinMaxInt(long.Parse(minText.Substring(1), CultureInfo.InvariantCulture),
                    long.MaxValue, output, lessDecimals, false);
                output.Append(')');
                if (c < '9')
                {
                    output.Append(" | ");
                    DigitRange((char)(c + 1), '9');
                    output.Append(' ');
                    MoreDigits(length - 1, lessDecimals);
                }
            }

            return;
        }

        if (hasMax)
        {
            if (maxValue >= 0)
            {
                if (topLevel)
                {
                    output.Append("\"-\" [1-9] ");
                    MoreDigits(0, lessDecimals);
                    output.Append(" | ");
                }

                BuildMinMaxInt(0, maxValue, output, decimalsLeft, true);
            }
            else
            {
                output.Append("\"-\" (");
                BuildMinMaxInt(-maxValue, long.MaxValue, output, decimalsLeft, false);
                output.Append(')');
            }

            return;
        }

        throw new GrammarException("At least one of the minimum or the maximum has to be set.");
    }

    /// <summary>
    /// Adds a rule under a name, making the name unique when a different rule already has it.
    /// </summary>
    /// <param name="name">The wanted name.</param>
    /// <param name="rule">The right-hand side.</param>
    /// <returns>The name the rule ended up with.</returns>
    private string AddRule(string name, string rule)
    {
        string escapedName = InvalidRuleCharacters.Replace(name, "-");
        string existing;
        if (!_rules.TryGetValue(escapedName, out existing) || existing == rule)
        {
            _rules[escapedName] = rule;
            return escapedName;
        }

        int i = 0;
        while (_rules.TryGetValue(escapedName + i.ToString(CultureInfo.InvariantCulture), out existing)
               && existing != rule)
        {
            i++;
        }

        string key = escapedName + i.ToString(CultureInfo.InvariantCulture);
        _rules[key] = rule;
        return key;
    }

    /// <summary>
    /// Adds a built-in rule and every built-in rule it depends on.
    /// </summary>
    /// <param name="name">The name to add it under.</param>
    /// <param name="rule">The built-in rule.</param>
    /// <returns>The name the rule ended up with.</returns>
    private string AddPrimitive(string name, GrammarBuiltinRule rule)
    {
        string added = AddRule(name, rule.Content);
        foreach (string dependency in rule.Dependencies)
        {
            GrammarBuiltinRule dependencyRule;
            if (!PrimitiveRules.TryGetValue(dependency, out dependencyRule)
                && !StringFormatRules.TryGetValue(dependency, out dependencyRule))
            {
                _errors.Add("Rule " + dependency + " not known");
                continue;
            }

            if (!_rules.ContainsKey(dependency))
            {
                AddPrimitive(dependency, dependencyRule);
            }
        }

        return added;
    }

    /// <summary>
    /// Writes the rule for a set of alternative schemas.
    /// </summary>
    /// <param name="name">The name the alternatives are derived from.</param>
    /// <param name="alternatives">The alternative schemas.</param>
    /// <returns>The alternation expression.</returns>
    private string GenerateUnionRule(string name, IList<JsonElement> alternatives)
    {
        List<string> rules = new List<string>();
        for (int i = 0; i < alternatives.Count; i++)
        {
            rules.Add(Visit(alternatives[i],
                name + (name.Length == 0 ? "alternative-" : "-") + i.ToString(CultureInfo.InvariantCulture)));
        }

        return string.Join(" | ", rules);
    }

    /// <summary>
    /// Writes the rule for a schema whose <c>type</c> lists several types, which means the same schema once
    /// per type.
    /// </summary>
    /// <param name="name">The name the alternatives are derived from.</param>
    /// <param name="schema">The schema.</param>
    /// <param name="types">The listed types.</param>
    /// <returns>The alternation expression.</returns>
    private string GenerateTypeUnionRule(string name, JsonElement schema, IList<JsonElement> types)
    {
        List<string> rules = new List<string>();
        for (int i = 0; i < types.Count; i++)
        {
            rules.Add(Visit(schema,
                name + (name.Length == 0 ? "alternative-" : "-") + i.ToString(CultureInfo.InvariantCulture),
                true, types[i]));
        }

        return string.Join(" | ", rules);
    }

    /// <summary>
    /// Writes a rule matching every JSON string except the given ones.
    /// </summary>
    /// <param name="strings">The strings to exclude.</param>
    /// <returns>The rule text.</returns>
    private string NotStrings(IList<string> strings)
    {
        GrammarTrieNode trie = new GrammarTrieNode();
        foreach (string value in strings)
        {
            trie.Insert(value);
        }

        string charRule = AddPrimitive("char", PrimitiveRules["char"]);
        StringBuilder output = new StringBuilder();
        output.Append("[\"] ( ");

        void VisitNode(GrammarTrieNode node)
        {
            StringBuilder rejects = new StringBuilder();
            bool first = true;
            foreach (KeyValuePair<char, GrammarTrieNode> pair in node.Children)
            {
                rejects.Append(pair.Key);
                if (first)
                {
                    first = false;
                }
                else
                {
                    output.Append(" | ");
                }

                output.Append('[').Append(pair.Key).Append(']');
                if (pair.Value.Children.Count > 0)
                {
                    output.Append(" (");
                    VisitNode(pair.Value);
                    output.Append(')');
                }
                else if (pair.Value.IsEndOfString)
                {
                    output.Append(' ').Append(charRule).Append('+');
                }
            }

            if (node.Children.Count > 0)
            {
                if (!first)
                {
                    output.Append(" | ");
                }

                output.Append("[^\"").Append(rejects.ToString()).Append("] ").Append(charRule).Append('*');
            }
        }

        VisitNode(trie);

        output.Append(" )");
        if (!trie.IsEndOfString)
        {
            output.Append('?');
        }

        output.Append(" [\"]");
        return output.ToString();
    }

    /// <summary>
    /// Collects the schemas every <c>$ref</c> in a document points at, so they can be visited on demand.
    /// </summary>
    /// <param name="node">The node being walked.</param>
    /// <param name="root">The document the pointers are relative to.</param>
    private void ResolveReferences(JsonElement node, JsonElement root)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in node.EnumerateArray())
            {
                ResolveReferences(item, root);
            }

            return;
        }

        if (node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        JsonElement reference;
        if (!node.TryGetProperty("$ref", out reference))
        {
            foreach (JsonProperty property in node.EnumerateObject())
            {
                ResolveReferences(property.Value, root);
            }

            return;
        }

        if (reference.ValueKind != JsonValueKind.String)
        {
            _errors.Add("Unsupported ref: " + reference.GetRawText());
            return;
        }

        string pointerText = reference.GetString();
        if (_references.ContainsKey(pointerText))
        {
            return;
        }

        if (!pointerText.StartsWith("#/", StringComparison.Ordinal))
        {
            //A remote $ref would have to be fetched, and a schema converter that does I/O is not something
            //this library offers. llama.cpp's own converter fetches; this one refuses.
            _errors.Add("Unsupported ref: " + pointerText);
            return;
        }

        JsonElement target = root;
        string pointer = pointerText.Substring(pointerText.IndexOf('#') + 1);
        string[] tokens = pointer.Split('/');
        for (int i = 1; i < tokens.Length; i++)
        {
            string selector = tokens[i];
            JsonElement child;
            if (target.ValueKind == JsonValueKind.Object && target.TryGetProperty(selector, out child))
            {
                target = child;
            }
            else if (target.ValueKind == JsonValueKind.Array)
            {
                int index;
                if (!int.TryParse(selector, NumberStyles.Integer, CultureInfo.InvariantCulture, out index)
                    || index < 0 || index >= target.GetArrayLength())
                {
                    _errors.Add("Error resolving ref " + pointerText + ": " + selector + " not in "
                                + target.GetRawText());
                    return;
                }

                target = target[index];
            }
            else
            {
                _errors.Add("Error resolving ref " + pointerText + ": " + selector + " not in "
                            + target.GetRawText());
                return;
            }
        }

        _references[pointerText] = target;
    }

    /// <summary>
    /// Adds the rule for a <c>$ref</c>, visiting the schema it points at the first time it is needed.
    /// </summary>
    /// <param name="pointerText">The pointer.</param>
    /// <returns>The name of the rule the reference resolves to.</returns>
    private string ResolveRef(string pointerText)
    {
        int hash = pointerText.IndexOf('#');
        string fragment = hash >= 0 ? pointerText.Substring(hash + 1) : pointerText;
        string referenceName = "ref" + InvalidRuleCharacters.Replace(fragment, "-");
        if (!_rules.ContainsKey(referenceName) && !_referencesBeingResolved.Contains(pointerText))
        {
            _referencesBeingResolved.Add(pointerText);
            JsonElement resolved;
            _references.TryGetValue(pointerText, out resolved);
            referenceName = Visit(resolved, referenceName);
            _referencesBeingResolved.Remove(pointerText);
        }

        return referenceName;
    }

    /// <summary>
    /// Writes the rule for an object schema.
    /// </summary>
    /// <param name="properties">The properties, in the order the schema wrote them.</param>
    /// <param name="required">The names of the required properties.</param>
    /// <param name="name">The name the sub-rules are derived from.</param>
    /// <param name="hasAdditionalProperties">Whether the schema said anything about additional properties.</param>
    /// <param name="additionalProperties">What it said.</param>
    /// <returns>The rule text.</returns>
    private string BuildObjectRule(IList<KeyValuePair<string, JsonElement>> properties,
        ISet<string> required, string name, bool hasAdditionalProperties, JsonElement additionalProperties)
    {
        List<string> requiredProperties = new List<string>();
        List<string> optionalProperties = new List<string>();
        Dictionary<string, string> propertyKeyValueRuleNames =
            new Dictionary<string, string>(StringComparer.Ordinal);
        List<string> propertyNames = new List<string>();

        foreach (KeyValuePair<string, JsonElement> pair in properties)
        {
            string propertyName = pair.Key;
            string propertyRuleName = Visit(pair.Value,
                name + (name.Length == 0 ? string.Empty : "-") + propertyName);
            propertyKeyValueRuleNames[propertyName] = AddRule(
                name + (name.Length == 0 ? string.Empty : "-") + propertyName + "-kv",
                FormatLiteral(CompactJson.DumpString(propertyName)) + " space \":\" space " + propertyRuleName);
            if (required.Contains(propertyName))
            {
                requiredProperties.Add(propertyName);
            }
            else
            {
                optionalProperties.Add(propertyName);
            }

            propertyNames.Add(propertyName);
        }

        bool additionalAllowed = hasAdditionalProperties
                                 && (additionalProperties.ValueKind == JsonValueKind.True
                                     || additionalProperties.ValueKind == JsonValueKind.Object);
        if (additionalAllowed)
        {
            string subName = name + (name.Length == 0 ? string.Empty : "-") + "additional";
            string valueRule = additionalProperties.ValueKind == JsonValueKind.Object
                ? Visit(additionalProperties, subName + "-value")
                : AddPrimitive("value", PrimitiveRules["value"]);

            string keyRule = propertyNames.Count == 0
                ? AddPrimitive("string", PrimitiveRules["string"])
                : AddRule(subName + "-k", NotStrings(propertyNames));
            string keyValueRule = AddRule(subName + "-kv", keyRule + " \":\" space " + valueRule);
            propertyKeyValueRuleNames["*"] = keyValueRule;
            optionalProperties.Add("*");
        }

        StringBuilder rule = new StringBuilder();
        rule.Append("\"{\" space ");
        for (int i = 0; i < requiredProperties.Count; i++)
        {
            if (i > 0)
            {
                rule.Append(" \",\" space ");
            }

            rule.Append(propertyKeyValueRuleNames[requiredProperties[i]]);
        }

        if (optionalProperties.Count > 0)
        {
            rule.Append(" (");
            if (requiredProperties.Count > 0)
            {
                rule.Append(" \",\" space ( ");
            }

            string GetRecursiveRefs(List<string> keys, bool firstIsOptional)
            {
                if (keys.Count == 0)
                {
                    return string.Empty;
                }

                string key = keys[0];
                string keyValueRuleName = propertyKeyValueRuleNames[key];
                string commaRef = "( \",\" space " + keyValueRuleName + " )";
                string result = firstIsOptional
                    ? commaRef + (key == "*" ? "*" : "?")
                    : keyValueRuleName + (key == "*" ? " " + commaRef + "*" : string.Empty);
                if (keys.Count > 1)
                {
                    result += " " + AddRule(
                        name + (name.Length == 0 ? string.Empty : "-") + key + "-rest",
                        GetRecursiveRefs(keys.GetRange(1, keys.Count - 1), true));
                }

                return result;
            }

            for (int i = 0; i < optionalProperties.Count; i++)
            {
                if (i > 0)
                {
                    rule.Append(" | ");
                }

                rule.Append(GetRecursiveRefs(
                    optionalProperties.GetRange(i, optionalProperties.Count - i), false));
            }

            if (requiredProperties.Count > 0)
            {
                rule.Append(" )");
            }

            rule.Append(" )?");
        }

        rule.Append(" space \"}\"");
        return rule.ToString();
    }

    /// <summary>
    /// Writes the rule for a constant value.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The literal rule.</returns>
    private static string GenerateConstantRule(JsonElement value)
    {
        return FormatLiteral(CompactJson.Dump(value));
    }

    /// <summary>
    /// Visits a schema and adds the rule for it.
    /// </summary>
    /// <param name="schema">The schema.</param>
    /// <param name="name">The name the rule and its sub-rules are derived from.</param>
    /// <returns>The name of the rule that matches the schema.</returns>
    private string Visit(JsonElement schema, string name)
    {
        return Visit(schema, name, false, default(JsonElement));
    }

    /// <summary>
    /// Visits a schema and adds the rule for it, optionally pretending its <c>type</c> is a single given
    /// type, which is how a schema listing several types is handled.
    /// </summary>
    /// <param name="schema">The schema.</param>
    /// <param name="name">The name the rule and its sub-rules are derived from.</param>
    /// <param name="hasTypeOverride">Whether <paramref name="typeOverride"/> stands in for the schema's type.</param>
    /// <param name="typeOverride">The type to use instead of the schema's own.</param>
    /// <returns>The name of the rule that matches the schema.</returns>
    private string Visit(JsonElement schema, string name, bool hasTypeOverride, JsonElement typeOverride)
    {
        JsonElement schemaType;
        bool hasType;
        if (hasTypeOverride)
        {
            schemaType = typeOverride;
            hasType = true;
        }
        else
        {
            hasType = TryGetMember(schema, "type", out schemaType);
        }

        bool typeIsNull = !hasType || schemaType.ValueKind == JsonValueKind.Null;
        bool TypeIs(string wanted)
        {
            return hasType && schemaType.ValueKind == JsonValueKind.String
                   && string.Equals(schemaType.GetString(), wanted, StringComparison.Ordinal);
        }

        JsonElement formatMember;
        string schemaFormat = TryGetMember(schema, "format", out formatMember)
                              && formatMember.ValueKind == JsonValueKind.String
            ? formatMember.GetString()
            : string.Empty;

        string ruleName = ReservedNames.Contains(name) ? name + "-" : name.Length == 0 ? "root" : name;

        JsonElement member;

        if (TryGetMember(schema, "$ref", out member))
        {
            string pointerText;
            if (!TryReadString(member, "\"$ref\" member", out pointerText))
            {
                return string.Empty;
            }

            return AddRule(ruleName, ResolveRef(pointerText));
        }

        bool isOneOf = TryGetMember(schema, "oneOf", out member);
        if (isOneOf || TryGetMember(schema, "anyOf", out member))
        {
            if (!RequireNonEmptyArray(member, isOneOf ? "\"oneOf\" member" : "\"anyOf\" member"))
            {
                return string.Empty;
            }

            return AddRule(ruleName, GenerateUnionRule(name, ToList(member)));
        }

        if (hasType && schemaType.ValueKind == JsonValueKind.Array)
        {
            if (!RequireNonEmptyArray(schemaType, "\"type\" member"))
            {
                return string.Empty;
            }

            return AddRule(ruleName, GenerateTypeUnionRule(name, schema, ToList(schemaType)));
        }

        if (TryGetMember(schema, "const", out member))
        {
            return AddRule(ruleName, GenerateConstantRule(member));
        }

        if (TryGetMember(schema, "enum", out member))
        {
            if (!RequireNonEmptyArray(member, "\"enum\" member"))
            {
                return string.Empty;
            }

            List<string> enumValues = new List<string>();
            foreach (JsonElement value in member.EnumerateArray())
            {
                enumValues.Add(GenerateConstantRule(value));
            }

            return AddRule(ruleName, "(" + string.Join(" | ", enumValues) + ")");
        }

        JsonElement additionalProperties;
        bool hasAdditionalProperties = TryGetMember(schema, "additionalProperties", out additionalProperties);
        if (hasAdditionalProperties
            && additionalProperties.ValueKind != JsonValueKind.True
            && additionalProperties.ValueKind != JsonValueKind.False
            && additionalProperties.ValueKind != JsonValueKind.Object)
        {
            AddKindError("\"additionalProperties\" member", "a boolean or a schema object",
                additionalProperties);
            return string.Empty;
        }

        if ((typeIsNull || TypeIs("object"))
            && (HasMember(schema, "properties")
                || (hasAdditionalProperties && additionalProperties.ValueKind != JsonValueKind.True)))
        {
            HashSet<string> required = new HashSet<string>(StringComparer.Ordinal);
            if (TryGetMember(schema, "required", out member))
            {
                if (!RequireArray(member, "\"required\" member"))
                {
                    return string.Empty;
                }

                foreach (JsonElement item in member.EnumerateArray())
                {
                    string requiredName;
                    if (!TryReadString(item, "name in the \"required\" member", out requiredName))
                    {
                        return string.Empty;
                    }

                    required.Add(requiredName);
                }
            }

            List<KeyValuePair<string, JsonElement>> properties =
                new List<KeyValuePair<string, JsonElement>>();
            if (TryGetMember(schema, "properties", out member))
            {
                if (!RequireObject(member, "\"properties\" member"))
                {
                    return string.Empty;
                }

                foreach (JsonProperty property in member.EnumerateObject())
                {
                    properties.Add(new KeyValuePair<string, JsonElement>(property.Name, property.Value));
                }
            }

            return AddRule(ruleName, BuildObjectRule(properties, required, name,
                hasAdditionalProperties, additionalProperties));
        }

        if ((typeIsNull || TypeIs("object") || TypeIs("string"))
            && TryGetMember(schema, "allOf", out member))
        {
            return AddRule(ruleName, BuildAllOfRule(member, name));
        }

        if ((typeIsNull || TypeIs("array"))
            && (HasMember(schema, "items") || HasMember(schema, "prefixItems")))
        {
            JsonElement items;
            if (!TryGetMember(schema, "items", out items))
            {
                TryGetMember(schema, "prefixItems", out items);
            }

            if (items.ValueKind == JsonValueKind.Array)
            {
                StringBuilder rule = new StringBuilder();
                rule.Append("\"[\" space ");
                int index = 0;
                foreach (JsonElement item in items.EnumerateArray())
                {
                    if (index > 0)
                    {
                        rule.Append(" \",\" space ");
                    }

                    rule.Append(Visit(item, name + (name.Length == 0 ? string.Empty : "-") + "tuple-"
                                            + index.ToString(CultureInfo.InvariantCulture)));
                    index++;
                }

                rule.Append(" space \"]\"");
                return AddRule(ruleName, rule.ToString());
            }

            string itemRuleName = Visit(items, name + (name.Length == 0 ? string.Empty : "-") + "item");
            int minItems = 0;
            if (TryGetMember(schema, "minItems", out member)
                && !TryReadInt32(member, "\"minItems\" member", out minItems))
            {
                return string.Empty;
            }

            int maxItems = int.MaxValue;
            if (TryGetMember(schema, "maxItems", out member)
                && !TryReadInt32(member, "\"maxItems\" member", out maxItems))
            {
                return string.Empty;
            }

            return AddRule(ruleName,
                "\"[\" space " + BuildRepetition(itemRuleName, minItems, maxItems, "\",\" space")
                + " space \"]\"");
        }

        if ((typeIsNull || TypeIs("string")) && TryGetMember(schema, "pattern", out member))
        {
            string pattern;
            if (!TryReadString(member, "\"pattern\" member", out pattern))
            {
                return string.Empty;
            }

            return VisitPattern(pattern, ruleName);
        }

        if ((typeIsNull || TypeIs("string")) && UuidFormat.IsMatch(schemaFormat))
        {
            return AddPrimitive(ruleName == "root" ? "root" : schemaFormat, PrimitiveRules["uuid"]);
        }

        if ((typeIsNull || TypeIs("string")) && StringFormatRules.ContainsKey(schemaFormat + "-string"))
        {
            string primitiveName = schemaFormat + "-string";
            return AddRule(ruleName, AddPrimitive(primitiveName, StringFormatRules[primitiveName]));
        }

        if (TypeIs("string") && (HasMember(schema, "minLength") || HasMember(schema, "maxLength")))
        {
            string charRule = AddPrimitive("char", PrimitiveRules["char"]);
            int minLength = 0;
            if (TryGetMember(schema, "minLength", out member)
                && !TryReadInt32(member, "\"minLength\" member", out minLength))
            {
                return string.Empty;
            }

            int maxLength = int.MaxValue;
            if (TryGetMember(schema, "maxLength", out member)
                && !TryReadInt32(member, "\"maxLength\" member", out maxLength))
            {
                return string.Empty;
            }

            return AddRule(ruleName,
                "\"\\\"\" " + BuildRepetition(charRule, minLength, maxLength) + " \"\\\"\"");
        }

        if (TypeIs("integer")
            && (HasMember(schema, "minimum") || HasMember(schema, "exclusiveMinimum")
                || HasMember(schema, "maximum") || HasMember(schema, "exclusiveMaximum")))
        {
            long minValue = long.MinValue;
            long maxValue = long.MaxValue;
            if (TryGetMember(schema, "minimum", out member))
            {
                if (!TryReadInt64(member, "\"minimum\" member", out minValue))
                {
                    return string.Empty;
                }
            }
            else if (TryGetMember(schema, "exclusiveMinimum", out member))
            {
                if (!TryReadInt64(member, "\"exclusiveMinimum\" member", out minValue))
                {
                    return string.Empty;
                }

                if (minValue == long.MaxValue)
                {
                    _errors.Add("The \"exclusiveMinimum\" member leaves no value in range.");
                    return string.Empty;
                }

                minValue = minValue + 1;
            }

            if (TryGetMember(schema, "maximum", out member))
            {
                if (!TryReadInt64(member, "\"maximum\" member", out maxValue))
                {
                    return string.Empty;
                }
            }
            else if (TryGetMember(schema, "exclusiveMaximum", out member))
            {
                if (!TryReadInt64(member, "\"exclusiveMaximum\" member", out maxValue))
                {
                    return string.Empty;
                }

                if (maxValue == long.MinValue)
                {
                    _errors.Add("The \"exclusiveMaximum\" member leaves no value in range.");
                    return string.Empty;
                }

                maxValue = maxValue - 1;
            }

            StringBuilder output = new StringBuilder();
            output.Append('(');
            BuildMinMaxInt(minValue, maxValue, output);
            output.Append(')');
            return AddRule(ruleName, output.ToString());
        }

        if (IsEmptySchema(schema) || TypeIs("object"))
        {
            return AddRule(ruleName, AddPrimitive("object", PrimitiveRules["object"]));
        }

        if (typeIsNull && schema.ValueKind == JsonValueKind.Object)
        {
            //No type and no structural keyword, for instance {"description": "..."}. JSON Schema reads that
            //as {} and accepts any value.
            return AddRule(ruleName, AddPrimitive("value", PrimitiveRules["value"]));
        }

        if (!hasType || schemaType.ValueKind != JsonValueKind.String
            || !PrimitiveRules.ContainsKey(schemaType.GetString()))
        {
            _errors.Add("Unrecognized schema: " + RawText(schema));
            return string.Empty;
        }

        return AddPrimitive(ruleName == "root" ? "root" : schemaType.GetString(),
            PrimitiveRules[schemaType.GetString()]);
    }

    /// <summary>
    /// Writes the rule for an <c>allOf</c> schema, which is treated as one object carrying every component's
    /// properties, or as the intersection of the components' enumerations when that is what they are.
    /// </summary>
    /// <param name="allOf">The <c>allOf</c> array.</param>
    /// <param name="name">The name the sub-rules are derived from.</param>
    /// <returns>The rule text.</returns>
    private string BuildAllOfRule(JsonElement allOf, string name)
    {
        HashSet<string> required = new HashSet<string>(StringComparer.Ordinal);
        List<KeyValuePair<string, JsonElement>> properties = new List<KeyValuePair<string, JsonElement>>();
        SortedDictionary<string, int> enumValues = new SortedDictionary<string, int>(StringComparer.Ordinal);

        void AddComponent(JsonElement componentSchema, bool isRequired)
        {
            JsonElement member;
            if (TryGetMember(componentSchema, "$ref", out member))
            {
                string pointerText;
                if (!TryReadString(member, "\"$ref\" member", out pointerText))
                {
                    return;
                }

                if (!_referencesBeingResolved.Add(pointerText))
                {
                    //The reference points back at a schema already being expanded here, so following it
                    //again would never end.
                    _errors.Add("Circular reference " + pointerText + " in \"allOf\".");
                    return;
                }

                try
                {
                    JsonElement resolved;
                    _references.TryGetValue(pointerText, out resolved);
                    AddComponent(resolved, isRequired);
                }
                finally
                {
                    _referencesBeingResolved.Remove(pointerText);
                }

                return;
            }

            if (TryGetMember(componentSchema, "properties", out member))
            {
                if (!RequireObject(member, "\"properties\" member"))
                {
                    return;
                }

                foreach (JsonProperty property in member.EnumerateObject())
                {
                    properties.Add(new KeyValuePair<string, JsonElement>(property.Name, property.Value));
                    if (isRequired)
                    {
                        required.Add(property.Name);
                    }
                }

                return;
            }

            if (TryGetMember(componentSchema, "enum", out member))
            {
                if (!RequireNonEmptyArray(member, "\"enum\" member"))
                {
                    return;
                }

                foreach (JsonElement value in member.EnumerateArray())
                {
                    string rule = GenerateConstantRule(value);
                    int count;
                    enumValues.TryGetValue(rule, out count);
                    enumValues[rule] = count + 1;
                }
            }
        }

        if (!RequireNonEmptyArray(allOf, "\"allOf\" member"))
        {
            return string.Empty;
        }

        int componentCount = 0;
        foreach (JsonElement component in allOf.EnumerateArray())
        {
            componentCount++;
            JsonElement anyOf;
            if (TryGetMember(component, "anyOf", out anyOf))
            {
                if (!RequireNonEmptyArray(anyOf, "\"anyOf\" member"))
                {
                    return string.Empty;
                }

                foreach (JsonElement alternative in anyOf.EnumerateArray())
                {
                    AddComponent(alternative, false);
                }
            }
            else
            {
                AddComponent(component, true);
            }
        }

        if (enumValues.Count > 0)
        {
            List<string> intersection = new List<string>();
            foreach (KeyValuePair<string, int> pair in enumValues)
            {
                if (pair.Value == componentCount)
                {
                    intersection.Add(pair.Key);
                }
            }

            if (intersection.Count > 0)
            {
                return "(" + string.Join(" | ", intersection) + ")";
            }
        }

        return BuildObjectRule(properties, required, name, false, default(JsonElement));
    }

    /// <summary>
    /// Turns a schema's regular-expression <c>pattern</c> into a grammar rule.
    /// </summary>
    /// <param name="pattern">The pattern, which has to be anchored at both ends.</param>
    /// <param name="name">The rule name.</param>
    /// <returns>The name of the rule that matches the pattern.</returns>
    private string VisitPattern(string pattern, string name)
    {
        if (pattern == null || pattern.Length < 2 || pattern[0] != '^'
            || pattern[pattern.Length - 1] != '$')
        {
            _errors.Add("Pattern must start with '^' and end with '$'");
            return string.Empty;
        }

        string subPattern = pattern.Substring(1, pattern.Length - 2);
        Dictionary<string, string> subRuleIds = new Dictionary<string, string>(StringComparer.Ordinal);

        int i = 0;
        int length = subPattern.Length;

        string ToRule((string Text, bool IsLiteral) item)
        {
            return item.IsLiteral ? "\"" + item.Text + "\"" : item.Text;
        }

        (string Text, bool IsLiteral) Transform()
        {
            int start = i;
            List<(string Text, bool IsLiteral)> sequence = new List<(string, bool)>();

            string GetDot()
            {
                string rule = _dotAll ? "[\\U00000000-\\U0010FFFF]" : "[^\\x0A\\x0D]";
                return AddRule("dot", rule);
            }

            (string Text, bool IsLiteral) JoinSequence()
            {
                List<(string Text, bool IsLiteral)> merged = new List<(string, bool)>();
                StringBuilder literal = new StringBuilder();

                void FlushLiteral()
                {
                    if (literal.Length == 0)
                    {
                        return;
                    }

                    merged.Add((literal.ToString(), true));
                    literal.Clear();
                }

                foreach ((string Text, bool IsLiteral) item in sequence)
                {
                    if (item.IsLiteral)
                    {
                        literal.Append(item.Text);
                    }
                    else
                    {
                        FlushLiteral();
                        merged.Add(item);
                    }
                }

                FlushLiteral();

                List<string> results = new List<string>();
                foreach ((string Text, bool IsLiteral) item in merged)
                {
                    results.Add(ToRule(item));
                }

                return (string.Join(" ", results), false);
            }

            while (i < length)
            {
                char c = subPattern[i];
                if (c == '.')
                {
                    sequence.Add((GetDot(), false));
                    i++;
                }
                else if (c == '(')
                {
                    i++;
                    if (i < length && subPattern[i] == '?')
                    {
                        if (i + 1 < length && subPattern[i + 1] == ':')
                        {
                            //A non-capturing group behaves like an ordinary one here.
                            i += 2;
                        }
                        else
                        {
                            //Lookahead and lookbehind are not expressible in a grammar, so the group is
                            //skipped over entirely rather than mistranslated. The C++ treats this as a
                            //warning rather than an error and still produces a grammar, and so does this.
                            _warnings.Add("Unsupported pattern syntax");
                            int depth = 1;
                            while (i < length && depth > 0)
                            {
                                if (subPattern[i] == '\\' && i + 1 < length)
                                {
                                    i += 2;
                                }
                                else
                                {
                                    if (subPattern[i] == '(')
                                    {
                                        depth++;
                                    }
                                    else if (subPattern[i] == ')')
                                    {
                                        depth--;
                                    }

                                    i++;
                                }
                            }

                            continue;
                        }
                    }

                    sequence.Add(("(" + ToRule(Transform()) + ")", false));
                }
                else if (c == ')')
                {
                    i++;
                    if (start > 0 && subPattern[start - 1] != '('
                        && (start < 2 || subPattern[start - 2] != '?' || subPattern[start - 1] != ':'))
                    {
                        _errors.Add("Unbalanced parentheses");
                    }

                    return JoinSequence();
                }
                else if (c == '[')
                {
                    StringBuilder squareBrackets = new StringBuilder();
                    squareBrackets.Append(c);
                    i++;
                    while (i < length && subPattern[i] != ']')
                    {
                        if (subPattern[i] == '\\')
                        {
                            squareBrackets.Append(subPattern, i, Math.Min(2, length - i));
                            i += 2;
                        }
                        else
                        {
                            squareBrackets.Append(subPattern[i]);
                            i++;
                        }
                    }

                    if (i >= length)
                    {
                        _errors.Add("Unbalanced square brackets");
                    }

                    squareBrackets.Append(']');
                    i++;
                    sequence.Add((squareBrackets.ToString(), false));
                }
                else if (c == '|')
                {
                    sequence.Add(("|", false));
                    i++;
                }
                else if (c == '*' || c == '+' || c == '?')
                {
                    if (sequence.Count > 0)
                    {
                        sequence[sequence.Count - 1] = (ToRule(sequence[sequence.Count - 1]) + c, false);
                    }

                    i++;
                }
                else if (c == '{')
                {
                    StringBuilder curlyBrackets = new StringBuilder();
                    curlyBrackets.Append(c);
                    i++;
                    while (i < length && subPattern[i] != '}')
                    {
                        curlyBrackets.Append(subPattern[i]);
                        i++;
                    }

                    if (i >= length)
                    {
                        _errors.Add("Unbalanced curly brackets");
                    }

                    curlyBrackets.Append('}');
                    i++;

                    string inner = curlyBrackets.ToString();
                    string[] numbers = inner.Substring(1, inner.Length - 2).Split(',');
                    int minTimes = 0;
                    int maxTimes = int.MaxValue;
                    if (numbers.Length == 1)
                    {
                        if (!TryParseRepetitionCount(numbers[0], out minTimes))
                        {
                            return (string.Empty, false);
                        }

                        maxTimes = minTimes;
                    }
                    else if (numbers.Length != 2)
                    {
                        _errors.Add("Wrong number of values in curly brackets");
                    }
                    else
                    {
                        if (numbers[0].Length > 0
                            && !TryParseRepetitionCount(numbers[0], out minTimes))
                        {
                            return (string.Empty, false);
                        }

                        if (numbers[1].Length > 0
                            && !TryParseRepetitionCount(numbers[1], out maxTimes))
                        {
                            return (string.Empty, false);
                        }
                    }

                    if (sequence.Count == 0)
                    {
                        continue;
                    }

                    (string Text, bool IsLiteral) last = sequence[sequence.Count - 1];
                    string sub = last.Text;
                    bool subIsLiteral = last.IsLiteral;

                    if (!subIsLiteral)
                    {
                        string subId;
                        if (!subRuleIds.TryGetValue(sub, out subId))
                        {
                            //The C++ takes a reference to the map slot, which creates the entry before the
                            //size is read, so the first generated name ends in 1 rather than 0.
                            subRuleIds[sub] = string.Empty;
                            subId = string.Empty;
                        }

                        if (subId.Length == 0)
                        {
                            subId = AddRule(
                                name + "-" + subRuleIds.Count.ToString(CultureInfo.InvariantCulture), sub);
                            subRuleIds[sub] = subId;
                        }

                        sub = subId;
                    }

                    sequence[sequence.Count - 1] = (
                        BuildRepetition(subIsLiteral ? "\"" + sub + "\"" : sub, minTimes, maxTimes, string.Empty),
                        false);
                }
                else
                {
                    StringBuilder literal = new StringBuilder();
                    while (i < length)
                    {
                        if (subPattern[i] == '\\' && i < length - 1)
                        {
                            char next = subPattern[i + 1];
                            if (Array.IndexOf(EscapedInRegexpsButNotInLiterals, next) >= 0)
                            {
                                i++;
                                literal.Append(subPattern[i]);
                                i++;
                            }
                            else
                            {
                                literal.Append(subPattern, i, 2);
                                i += 2;
                            }
                        }
                        else if (subPattern[i] == '"')
                        {
                            literal.Append("\\\"");
                            i++;
                        }
                        else if (Array.IndexOf(NonLiteralCharacters, subPattern[i]) < 0
                                 && (i == length - 1 || literal.Length == 0 || subPattern[i + 1] == '.'
                                     || Array.IndexOf(NonLiteralCharacters, subPattern[i + 1]) < 0))
                        {
                            literal.Append(subPattern[i]);
                            i++;
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (literal.Length > 0)
                    {
                        sequence.Add((literal.ToString(), true));
                    }
                }
            }

            return JoinSequence();
        }

        return AddRule(name, "\"\\\"\" (" + ToRule(Transform()) + ") \"\\\"\"");
    }

    /// <summary>
    /// Records that a schema member is of the wrong JSON kind.
    /// </summary>
    /// <param name="what">What was being read, for instance <c>"required" member</c>.</param>
    /// <param name="expected">The kind it had to be, for instance <c>an array</c>.</param>
    /// <param name="value">What was there instead.</param>
    private void AddKindError(string what, string expected, JsonElement value)
    {
        _errors.Add("The " + what + " has to be " + expected + ", but it is " + RawText(value) + ".");
    }

    /// <summary>
    /// Reads a schema member that has to be a JSON string.
    /// </summary>
    /// <param name="value">The member.</param>
    /// <param name="what">What is being read, for the error message.</param>
    /// <param name="text">Receives the text.</param>
    /// <returns><see langword="true"/> when it was a string; otherwise an error has been recorded.</returns>
    private bool TryReadString(JsonElement value, string what, out string text)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            AddKindError(what, "a string", value);
            text = string.Empty;
            return false;
        }

        text = value.GetString();
        return true;
    }

    /// <summary>
    /// Reads a schema member that has to be a whole number fitting in 32 bits.
    /// </summary>
    /// <param name="value">The member.</param>
    /// <param name="what">What is being read, for the error message.</param>
    /// <param name="number">Receives the number.</param>
    /// <returns><see langword="true"/> when it was one; otherwise an error has been recorded.</returns>
    private bool TryReadInt32(JsonElement value, string what, out int number)
    {
        if (value.ValueKind != JsonValueKind.Number)
        {
            AddKindError(what, "a number", value);
            number = 0;
            return false;
        }

        if (!value.TryGetInt32(out number))
        {
            AddKindError(what, "a whole number that fits in 32 bits", value);
            number = 0;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Reads a schema member that has to be a whole number fitting in 64 bits.
    /// </summary>
    /// <param name="value">The member.</param>
    /// <param name="what">What is being read, for the error message.</param>
    /// <param name="number">Receives the number.</param>
    /// <returns><see langword="true"/> when it was one; otherwise an error has been recorded.</returns>
    private bool TryReadInt64(JsonElement value, string what, out long number)
    {
        if (value.ValueKind != JsonValueKind.Number)
        {
            AddKindError(what, "a number", value);
            number = 0;
            return false;
        }

        if (!value.TryGetInt64(out number))
        {
            AddKindError(what, "a whole number that fits in 64 bits", value);
            number = 0;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Checks that a schema member is a JSON array.
    /// </summary>
    /// <param name="value">The member.</param>
    /// <param name="what">What is being read, for the error message.</param>
    /// <returns><see langword="true"/> when it is; otherwise an error has been recorded.</returns>
    private bool RequireArray(JsonElement value, string what)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            AddKindError(what, "an array", value);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Checks that a schema member is a JSON array with something in it, because an empty one would make a
    /// rule that matches nothing at all.
    /// </summary>
    /// <param name="value">The member.</param>
    /// <param name="what">What is being read, for the error message.</param>
    /// <returns><see langword="true"/> when it is; otherwise an error has been recorded.</returns>
    private bool RequireNonEmptyArray(JsonElement value, string what)
    {
        if (!RequireArray(value, what))
        {
            return false;
        }

        if (value.GetArrayLength() == 0)
        {
            _errors.Add("The " + what + " has to list at least one entry, but it is empty.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Checks that a schema member is a JSON object.
    /// </summary>
    /// <param name="value">The member.</param>
    /// <param name="what">What is being read, for the error message.</param>
    /// <returns><see langword="true"/> when it is; otherwise an error has been recorded.</returns>
    private bool RequireObject(JsonElement value, string what)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            AddKindError(what, "an object", value);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Reads one of the counts in a pattern's <c>{m,n}</c> repetition.
    /// </summary>
    /// <param name="text">The text between the brackets.</param>
    /// <param name="value">Receives the count.</param>
    /// <returns><see langword="true"/> when it was a count that fits in 32 bits.</returns>
    private bool TryParseRepetitionCount(string text, out int value)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            _errors.Add("The repetition count \"" + text
                        + "\" in a pattern is not a whole number that fits in 32 bits.");
            value = 0;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Throws when the conversion collected any errors. Warnings - a pattern the converter had to widen,
    /// for instance - leave the grammar usable and are collected in <see cref="Warnings"/> instead.
    /// </summary>
    /// <exception cref="GrammarException">The schema uses something the converter cannot express.</exception>
    private void CheckErrors()
    {
        if (_errors.Count > 0)
        {
            throw new GrammarException("JSON schema conversion failed:" + Environment.NewLine
                                       + string.Join(Environment.NewLine, _errors));
        }
    }

    /// <summary>
    /// Writes the collected rules out as grammar text.
    /// </summary>
    /// <returns>The grammar.</returns>
    private string FormatGrammar()
    {
        StringBuilder builder = new StringBuilder();
        foreach (KeyValuePair<string, string> pair in _rules)
        {
            builder.Append(pair.Key).Append(" ::= ").Append(pair.Value).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reads a member of a schema object.
    /// </summary>
    /// <param name="schema">The schema.</param>
    /// <param name="name">The member name.</param>
    /// <param name="value">Receives the member.</param>
    /// <returns><see langword="true"/> when the schema is an object carrying that member.</returns>
    private static bool TryGetMember(JsonElement schema, string name, out JsonElement value)
    {
        if (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty(name, out value))
        {
            return true;
        }

        value = default(JsonElement);
        return false;
    }

    /// <summary>
    /// Reports whether a schema object carries a member.
    /// </summary>
    /// <param name="schema">The schema.</param>
    /// <param name="name">The member name.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private static bool HasMember(JsonElement schema, string name)
    {
        JsonElement ignored;
        return TryGetMember(schema, name, out ignored);
    }

    /// <summary>
    /// Reports whether a schema is the empty schema, which accepts any JSON object.
    /// </summary>
    /// <param name="schema">The schema.</param>
    /// <returns><see langword="true"/> when it has nothing in it.</returns>
    private static bool IsEmptySchema(JsonElement schema)
    {
        switch (schema.ValueKind)
        {
            case JsonValueKind.Undefined:
            case JsonValueKind.Null:
                return true;
            case JsonValueKind.Object:
                foreach (JsonProperty ignored in schema.EnumerateObject())
                {
                    return false;
                }

                return true;
            case JsonValueKind.Array:
                return schema.GetArrayLength() == 0;
            default:
                return false;
        }
    }

    /// <summary>
    /// Copies a JSON array into a list.
    /// </summary>
    /// <param name="array">The array.</param>
    /// <returns>The items.</returns>
    private static IList<JsonElement> ToList(JsonElement array)
    {
        List<JsonElement> items = new List<JsonElement>();
        if (array.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in array.EnumerateArray())
            {
                items.Add(item);
            }
        }

        return items;
    }

    /// <summary>
    /// Renders a schema back as text, for an error message.
    /// </summary>
    /// <param name="schema">The schema.</param>
    /// <returns>The JSON text.</returns>
    private static string RawText(JsonElement schema)
    {
        return schema.ValueKind == JsonValueKind.Undefined ? "null" : schema.GetRawText();
    }
}
