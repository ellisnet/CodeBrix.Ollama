using System;

namespace CodeBrix.Ollama.ModelRunner.Tests;

internal sealed class MuseCocoTestProgress : IProgress<int>
{
    private readonly Action<int> _report;
    internal MuseCocoTestProgress(Action<int> report) => _report = report;
    public void Report(int value) => _report(value);
}
