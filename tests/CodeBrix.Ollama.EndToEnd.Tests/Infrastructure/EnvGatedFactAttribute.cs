using System;
using System.Runtime.CompilerServices;
using Xunit;

namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>
/// A fact that is skipped unless an environment variable is set to the expected value, or unless every one of
/// several variables is set to "1". It is a copy of the attribute the other test projects carry rather than a
/// shared one, because no test project references another and neither library may grow a dependency to let
/// them.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class EnvGatedFactAttribute : FactAttribute
{
    /// <summary>Creates the attribute, setting <see cref="FactAttribute.Skip"/> when the gate is closed.</summary>
    /// <param name="variable">The environment variable that opens the gate.</param>
    /// <param name="expectedValue">The value the variable must carry.</param>
    /// <param name="sourceFilePath">Supplied by the compiler.</param>
    /// <param name="sourceLineNumber">Supplied by the compiler.</param>
    public EnvGatedFactAttribute(string variable, string expectedValue = "1",
        [CallerFilePath] string sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        string actual = Environment.GetEnvironmentVariable(variable);
        if (!string.Equals(actual, expectedValue, StringComparison.Ordinal))
        {
            Skip = $"Set {variable}={expectedValue} to run this test.";
        }
    }

    /// <summary>Creates the attribute, setting <see cref="FactAttribute.Skip"/> when any gate is closed.</summary>
    /// <param name="variables">The environment variables that must all carry "1".</param>
    /// <param name="sourceFilePath">Supplied by the compiler.</param>
    /// <param name="sourceLineNumber">Supplied by the compiler.</param>
    public EnvGatedFactAttribute(string[] variables,
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
    }
}
