// Copyright 2011 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/template.go (BSD-3-Clause);

/// <summary>
/// A parsed Go text/template together with the function table it was parsed against. A template text
/// may define further named templates; they are all reachable from here.
/// </summary>
internal sealed class GoTemplate
{
    private GoTemplate(string name, Dictionary<string, GoTemplateTree> trees,
        IReadOnlyDictionary<string, GoFunction> funcs)
    {
        Name = name;
        Trees = trees;
        Funcs = funcs;
    }

    /// <summary>The name of the top-level template.</summary>
    internal string Name { get; }

    /// <summary>Every tree the template text produced, keyed by template name.</summary>
    internal Dictionary<string, GoTemplateTree> Trees { get; }

    /// <summary>The functions the template may call, keyed by name.</summary>
    internal IReadOnlyDictionary<string, GoFunction> Funcs { get; }

    /// <summary>The top-level tree.</summary>
    internal GoTemplateTree Tree => Trees[Name];

    /// <summary>The root node of the top-level tree.</summary>
    internal GoListNode Root => Tree.Root;

    /// <summary>
    /// Parses template text against a function table.
    /// </summary>
    /// <param name="name">The name to give the top-level template.</param>
    /// <param name="text">The template text.</param>
    /// <param name="funcs">The functions the template may call, on top of Go's own built-ins.</param>
    /// <returns>The parsed template.</returns>
    /// <exception cref="ChatTemplateException">The template text is not valid.</exception>
    internal static GoTemplate Parse(string name, string text, IReadOnlyDictionary<string, GoFunction> funcs)
    {
        Dictionary<string, GoFunction> table = new Dictionary<string, GoFunction>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, GoFunction> builtin in GoFuncs.Builtins)
        {
            table[builtin.Key] = builtin.Value;
        }

        if (funcs != null)
        {
            foreach (KeyValuePair<string, GoFunction> custom in funcs)
            {
                table[custom.Key] = custom.Value;
            }
        }

        HashSet<string> names = new HashSet<string>(table.Keys, StringComparer.Ordinal);
        Dictionary<string, GoTemplateTree> trees = GoTemplateTree.Parse(name, text, names);
        return new GoTemplate(name, trees, table);
    }

    /// <summary>
    /// Builds a template around trees that were assembled by hand rather than parsed, which is how
    /// Ollama renders a template it has rewritten.
    /// </summary>
    /// <param name="name">The name of the top-level template.</param>
    /// <param name="root">The root node of the top-level tree.</param>
    /// <param name="funcs">The functions the template may call.</param>
    /// <returns>The template.</returns>
    internal static GoTemplate FromRoot(string name, GoListNode root, IReadOnlyDictionary<string, GoFunction> funcs)
    {
        Dictionary<string, GoTemplateTree> trees = GoTemplateTree.FromRoot(name, root);
        return new GoTemplate(name, trees, funcs);
    }

    /// <summary>
    /// Finds an associated template by name.
    /// </summary>
    /// <param name="name">The template name.</param>
    /// <returns>The tree, or <see langword="null"/> when there is no such template.</returns>
    internal GoTemplateTree Lookup(string name)
        => Trees.TryGetValue(name, out GoTemplateTree tree) ? tree : null;

    /// <summary>
    /// Renders the template.
    /// </summary>
    /// <param name="data">The value dot starts out as.</param>
    /// <returns>The rendered text.</returns>
    /// <exception cref="ChatTemplateException">The template could not be rendered.</exception>
    internal string Execute(object data) => new GoTemplateExecutor(this).Execute(data);
}
