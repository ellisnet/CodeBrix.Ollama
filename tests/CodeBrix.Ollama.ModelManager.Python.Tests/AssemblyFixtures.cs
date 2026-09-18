using CodeBrix.Ollama.ModelManager.Python.Tests;
using Xunit;

//One interpreter for the whole assembly: started on first use and shut down once, at the end.
[assembly: AssemblyFixture(typeof(PythonTestFixture))]
