using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp common/json-schema-to-grammar.cpp;

/// <summary>
/// A node of the character trie the schema converter builds to write a rule that matches every JSON string
/// except a given set of them, which is how an object with <c>additionalProperties</c> keeps its extra keys
/// from colliding with the properties it already names.
/// </summary>
/// <remarks>
/// The children are kept in character order, matching the ordered map the C++ original uses, so the
/// alternatives come out in the same order and the expected grammars carry across unchanged.
/// </remarks>
internal sealed class GrammarTrieNode
{
    /// <summary>The child nodes, in character order.</summary>
    public SortedDictionary<char, GrammarTrieNode> Children { get; } =
        new SortedDictionary<char, GrammarTrieNode>();

    /// <summary>Whether a string of the set ends at this node.</summary>
    public bool IsEndOfString { get; set; }

    /// <summary>
    /// Adds a string to the trie.
    /// </summary>
    /// <param name="value">The string.</param>
    public void Insert(string value)
    {
        GrammarTrieNode node = this;
        foreach (char c in value)
        {
            GrammarTrieNode child;
            if (!node.Children.TryGetValue(c, out child))
            {
                child = new GrammarTrieNode();
                node.Children[c] = child;
            }

            node = child;
        }

        node.IsEndOfString = true;
    }
}
