using System;

namespace ModelQueryTool.Helpers;

/// <summary>
/// An <see cref="IProgress{T}"/> that hands each report straight to a delegate, on whatever
/// thread reported it.
/// </summary>
/// <typeparam name="T">What is being reported.</typeparam>
/// <remarks>
/// The framework's own <see cref="Progress{T}"/> posts its reports to the context it was created
/// on, which both reorders them and makes a test wait for a thread pool. Everything this
/// application does with a report is safe from any thread - it ends at the view model, which
/// marshals - so the report is better delivered where it was made.
/// </remarks>
internal sealed class DelegateProgress<T> : IProgress<T>
{
    private readonly Action<T> _report;

    /// <summary>Creates the reporter.</summary>
    /// <param name="report">What to do with each report.</param>
    /// <exception cref="ArgumentNullException"><paramref name="report"/> is null.</exception>
    public DelegateProgress(Action<T> report)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));
    }

    /// <inheritdoc />
    public void Report(T value) => _report(value);
}
