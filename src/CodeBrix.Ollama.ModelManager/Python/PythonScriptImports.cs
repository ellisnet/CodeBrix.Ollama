using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The allow-list every script that ships inside this package is read against before it runs: a script
/// may import only the modules named here, and may never import everything out of a module.
/// </summary>
/// <remarks>
/// <para>
/// This is a check on THIS package's own scripts, not a sandbox for somebody else's: the scripts are
/// embedded resources, so the only way an unlisted import reaches this check is a change to this
/// repository, and the check is what makes that change visible in a test rather than at run time.
/// </para>
/// <para>
/// The reading is deliberately literal - the lines a script actually begins with - and matches the
/// shape of the reference implementation's own import policy.
/// </para>
/// </remarks>
internal static class PythonScriptImports
{
    /// <summary>
    /// The modules the scripts in this package may import. Every later feature that ships a script adds
    /// what that script needs, and nothing else.
    /// </summary>
    private static readonly HashSet<string> PermittedModules = new HashSet<string>(StringComparer.Ordinal)
    {
        "onnx",
        "onnxruntime",
        "onnxruntime_genai",
        "optimum",
        "os",
        "sys",
    };

    /// <summary>
    /// The modules a script in this package may import.
    /// </summary>
    internal static IReadOnlyCollection<string> Permitted => PermittedModules;

    /// <summary>
    /// Whether a module may be imported by a script that ships in this package.
    /// </summary>
    /// <param name="moduleName">The module name as the script writes it, dotted parts and all.</param>
    /// <returns><see langword="true"/> when the module, or the package it belongs to, is allowed.</returns>
    internal static bool IsPermitted(string moduleName)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
        {
            return false;
        }

        string trimmed = moduleName.Trim();
        if (trimmed == "*")
        {
            return false;
        }

        string root = trimmed.Split('.')[0];
        return PermittedModules.Contains(root);
    }

    /// <summary>
    /// Finds the first import line of a script that the allow-list refuses.
    /// </summary>
    /// <param name="scriptText">The script to read.</param>
    /// <param name="offendingLine">The line that was refused, trimmed; <see langword="null"/> when none was.</param>
    /// <returns><see langword="true"/> when a line was refused.</returns>
    internal static bool TryFindForbiddenImport(string scriptText, out string offendingLine)
    {
        offendingLine = null;

        if (string.IsNullOrWhiteSpace(scriptText))
        {
            return false;
        }

        foreach (string raw in PythonScripts.NormalizeLineEndings(scriptText).Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (line.StartsWith("from ", StringComparison.Ordinal))
            {
                int marker = line.IndexOf(" import ", StringComparison.Ordinal);
                if (marker < 0)
                {
                    continue;
                }

                string module = line.Substring("from ".Length, marker - "from ".Length).Trim();
                string names = line.Substring(marker + " import ".Length).Trim();
                if (!IsPermitted(module) || NamesEverything(names))
                {
                    offendingLine = line;
                    return true;
                }
                continue;
            }

            if (!line.StartsWith("import ", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (string part in line.Substring("import ".Length).Split(','))
            {
                string module = StripAlias(part);
                if (!IsPermitted(module))
                {
                    offendingLine = line;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Reads a script and refuses it if it imports anything the allow-list does not name.
    /// </summary>
    /// <param name="scriptName">The script's name, for the message.</param>
    /// <param name="scriptText">The script to check.</param>
    /// <exception cref="ModelManagerException">The script imports something that is not allowed.</exception>
    internal static void Check(string scriptName, string scriptText)
    {
        if (TryFindForbiddenImport(scriptText, out string offendingLine))
        {
            throw new ModelManagerException(
                "the Python script '" + scriptName + "' is not allowed to run the import line '"
                + offendingLine + "'; the modules this package's scripts may import are "
                + string.Join(", ", PermittedModules));
        }
    }

    /// <summary>
    /// Whether the names an import line asks for amount to "everything in that module".
    /// </summary>
    /// <param name="names">The text after the <c>import</c> keyword.</param>
    /// <returns><see langword="true"/> when the line is a star import.</returns>
    private static bool NamesEverything(string names)
    {
        foreach (string part in (names ?? string.Empty).Split(','))
        {
            if (StripAlias(part) == "*")
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Removes an <c>as</c> alias and surrounding space from one entry of an import list.
    /// </summary>
    /// <param name="part">One comma-separated entry.</param>
    /// <returns>The module or name being imported.</returns>
    private static string StripAlias(string part)
    {
        string trimmed = (part ?? string.Empty).Trim();
        int alias = trimmed.IndexOf(" as ", StringComparison.Ordinal);
        return alias < 0 ? trimmed : trimmed.Substring(0, alias).Trim();
    }
}
