using System;
using System.Collections.Generic;

namespace ModelQueryTool.ModelRunning.Tests.Fakes;

/// <summary>Keeps every fraction it is told about, in order, with no marshalling of any kind.</summary>
public sealed class RecordingProgress : IProgress<float>
{
    private readonly List<float> _fractions = new List<float>();

    /// <summary>Gets the fractions reported so far.</summary>
    public IReadOnlyList<float> Fractions
    {
        get
        {
            lock (_fractions)
            {
                return _fractions.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public void Report(float value)
    {
        lock (_fractions)
        {
            _fractions.Add(value);
        }
    }
}
