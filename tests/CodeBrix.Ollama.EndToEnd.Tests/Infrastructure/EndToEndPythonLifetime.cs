using System;
using CodeBrix.Ollama.ModelManager;
using Xunit;

[assembly: AssemblyFixture(typeof(CodeBrix.Ollama.EndToEnd.Tests.EndToEndPythonLifetime))]

namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>Explicitly ends a Manager-owned interpreter after every test has finished using it.</summary>
public sealed class EndToEndPythonLifetime : IDisposable
{
    /// <summary>No-op if this assembly never initialized Python or the host owns it.</summary>
    public void Dispose() => PythonSupport.Shutdown();
}
