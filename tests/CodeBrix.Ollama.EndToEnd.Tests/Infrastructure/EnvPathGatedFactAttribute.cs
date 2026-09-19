using System;
using System.Runtime.CompilerServices;
using Xunit;

namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>
/// A fact that needs a PATH as well as its gates: every named gate must be set to "1", and one further
/// variable must name something. It is a separate attribute rather than another constructor on
/// <see cref="EnvGatedFactAttribute"/> because the two would be ambiguous - a gate list and a variable name
/// are both strings - and because what it says is different: this test needs a folder that this repository
/// cannot ship and will never reach for on its own.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class EnvPathGatedFactAttribute : FactAttribute
{
    /// <summary>Creates the attribute, setting <see cref="FactAttribute.Skip"/> when anything is missing.</summary>
    /// <param name="pathVariable">The variable that must name a folder.</param>
    /// <param name="variables">The environment variables that must all carry "1".</param>
    /// <param name="sourceFilePath">Supplied by the compiler.</param>
    /// <param name="sourceLineNumber">Supplied by the compiler.</param>
    public EnvPathGatedFactAttribute(string pathVariable, string[] variables,
        [CallerFilePath] string sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        foreach (string variable in variables)
        {
            string actual = Environment.GetEnvironmentVariable(variable);
            if (!string.Equals(actual, "1", StringComparison.Ordinal))
            {
                Skip = $"Set {string.Join("=1 and ", variables)}=1 to run this test.";
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(pathVariable)))
        {
            Skip = $"Set {pathVariable} to the folder it names to run this test.";
        }
    }
}
