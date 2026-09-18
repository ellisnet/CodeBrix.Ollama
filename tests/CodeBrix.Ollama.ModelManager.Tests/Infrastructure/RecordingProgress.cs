using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// An <see cref="IProgress{T}"/> that writes down every status it is handed, in the order it was handed
/// them, on the thread that reported it.
/// </summary>
/// <remarks>
/// It exists because <see cref="Progress{T}"/> DOES NOT KEEP ORDER: with no synchronization context it
/// posts each report to the thread pool, so a listener can see the last status before an earlier one and
/// a test that asserts what came last fails now and then for a reason that has nothing to do with the
/// code it covers. Reporting is synchronous here, so the list is exactly what the operation reported.
/// </remarks>
public sealed class RecordingProgress : IProgress<PullProgress>
{
    private readonly List<string> _statuses = new List<string>();

    /// <summary>The statuses, in the order they were reported.</summary>
    public IReadOnlyList<string> Statuses
    {
        get { return _statuses; }
    }

    /// <summary>
    /// Writes one status down.
    /// </summary>
    /// <param name="value">What was reported.</param>
    public void Report(PullProgress value)
    {
        if (value != null)
        {
            _statuses.Add(value.Status);
        }
    }
}
