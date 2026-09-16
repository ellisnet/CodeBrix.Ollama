using System;
using System.Runtime.CompilerServices;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// A fact that is skipped unless an environment variable is set to the expected value. Used for the
/// live tests that really download from a registry.
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
        var actual = Environment.GetEnvironmentVariable(variable);
        if (!string.Equals(actual, expectedValue, StringComparison.Ordinal))
        {
            Skip = $"Set {variable}={expectedValue} to run this test.";
        }
    }
}
