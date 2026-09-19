using System;
using System.Runtime.CompilerServices;
using Xunit;

namespace ModelQueryTool.Core.Tests.Infrastructure;

/// <summary>
/// A fact that is skipped unless an environment variable carries "1". The variable IS the guard:
/// a test behind one acts on the real folder and the real model, and nothing else stands between
/// it and them.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class EnvGatedFactAttribute : FactAttribute
{
    /// <summary>Creates the attribute, setting <see cref="FactAttribute.Skip"/> when the gate is shut.</summary>
    /// <param name="variable">The environment variable that must carry "1".</param>
    /// <param name="sourceFilePath">Supplied by the compiler.</param>
    /// <param name="sourceLineNumber">Supplied by the compiler.</param>
    public EnvGatedFactAttribute(
        string variable,
        [CallerFilePath] string sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        var actual = Environment.GetEnvironmentVariable(variable);

        if (!string.Equals(actual, "1", StringComparison.Ordinal))
        {
            Skip = "Set " + variable + "=1 to run this test.";
        }
    }
}
