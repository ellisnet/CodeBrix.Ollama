using System;
using System.Collections.Generic;
using ModelQueryTool.ModelAccess.Models;

namespace ModelQueryTool.ModelAccess.Tests;

/// <summary>
/// Keeps every progress report a staging run made, so that a test can look at the whole run rather than
/// at one moment of it.
/// </summary>
public sealed class RecordingProgress : IProgress<ModelStagingProgress>
{
    private readonly List<ModelStagingProgress> _reports = new List<ModelStagingProgress>();
    private readonly object _sync = new object();

    /// <summary>Gets every report, oldest first.</summary>
    public IReadOnlyList<ModelStagingProgress> Reports
    {
        get
        {
            lock (_sync)
            {
                return _reports.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public void Report(ModelStagingProgress value)
    {
        lock (_sync)
        {
            _reports.Add(value);
        }
    }
}
