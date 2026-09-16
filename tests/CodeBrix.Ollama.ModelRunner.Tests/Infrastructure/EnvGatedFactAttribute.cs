using System;
using System.Runtime.CompilerServices;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// A fact that is skipped unless every named environment variable is set to "1". Used for the live tests
/// that download and run real models.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class EnvGatedFactAttribute : FactAttribute
{
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

    /// <summary>Creates the attribute for a single gate.</summary>
    /// <param name="variable">The environment variable that must carry "1".</param>
    /// <param name="sourceFilePath">Supplied by the compiler.</param>
    /// <param name="sourceLineNumber">Supplied by the compiler.</param>
    public EnvGatedFactAttribute(string variable,
        [CallerFilePath] string sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : this(new[] { variable }, sourceFilePath, sourceLineNumber)
    {
    }
}
