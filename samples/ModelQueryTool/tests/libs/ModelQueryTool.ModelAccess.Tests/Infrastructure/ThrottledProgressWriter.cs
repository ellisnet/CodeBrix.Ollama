using System;
using System.Diagnostics;
using System.Globalization;
using ModelQueryTool.ModelAccess.Models;
using Xunit;

namespace ModelQueryTool.ModelAccess.Tests;

/// <summary>
/// Writes a staging run's progress to a test's output, at most one line every so often, so that a
/// download of twenty minutes says where it has got to without writing thousands of lines.
/// </summary>
public sealed class ThrottledProgressWriter : IProgress<ModelStagingProgress>
{
    /// <summary>How many bytes are in a gibibyte.</summary>
    private const double BytesPerGibibyte = 1024d * 1024d * 1024d;

    private readonly ITestOutputHelper _output;
    private readonly TimeSpan _interval;
    private readonly Stopwatch _sinceLastLine = Stopwatch.StartNew();
    private readonly object _sync = new object();
    private int _lineCount;
    private bool _finished;

    /// <summary>
    /// Creates a writer.
    /// </summary>
    /// <param name="output">Where the lines go.</param>
    /// <param name="interval">The shortest gap between two lines.</param>
    public ThrottledProgressWriter(ITestOutputHelper output, TimeSpan interval)
    {
        _output = output;
        _interval = interval;
    }

    /// <summary>Gets how many lines have been written.</summary>
    public int LineCount
    {
        get
        {
            lock (_sync)
            {
                return _lineCount;
            }
        }
    }

    /// <inheritdoc />
    public void Report(ModelStagingProgress value)
    {
        if (value == null)
        {
            return;
        }

        lock (_sync)
        {
            bool isLast = value.OverallPercent >= 100d;
            if (isLast && _finished)
            {
                return;
            }
            if (!isLast && _lineCount > 0 && _sinceLastLine.Elapsed < _interval)
            {
                return;
            }

            _finished = _finished || isLast;
            _lineCount++;
            _sinceLastLine.Restart();

            _output.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,-26} {1,8:F2} / {2:F2} GiB  {3,6:F2}%",
                value.Status,
                value.OverallCompletedBytes / BytesPerGibibyte,
                value.OverallTotalBytes / BytesPerGibibyte,
                value.OverallPercent));
        }
    }
}
