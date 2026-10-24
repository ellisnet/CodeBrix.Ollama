using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The running account of one render against its <see cref="TemplateLimits"/>: steps taken, nesting depth,
/// elapsed time and cancellation. Both template engines charge it as they go.
/// </summary>
/// <remarks>
/// <para>
/// A render is synchronous from start to finish on one thread, so the budget in force is kept in a
/// thread-static slot for the length of the render. That lets the value helpers deep inside a filter or a
/// format verb check a size without every one of them being handed the budget. A helper called outside any
/// render is held to the default limits.
/// </para>
/// <para>
/// Opening a budget while one is already open on the thread reuses the open one, so a render that renders
/// its template several times - the older Ollama shape does, once per exchange - is held to ONE budget.
/// </para>
/// </remarks>
internal sealed class TemplateBudget
{
    /// <summary>How many steps or polls pass between looks at the clock and the cancellation token.</summary>
    private const int CheckInterval = 1024;

    [ThreadStatic]
    private static TemplateBudget current;

    private readonly CancellationToken cancellationToken;
    private readonly long deadline;
    private long steps;
    private long polls;
    private int depth;

    private TemplateBudget(TemplateLimits limits, CancellationToken cancellationToken)
    {
        Limits = limits;
        this.cancellationToken = cancellationToken;
        deadline = long.MaxValue;
        if (limits.RenderTimeout.HasValue)
        {
            long now = Stopwatch.GetTimestamp();
            double ticks = limits.RenderTimeout.Value.TotalSeconds * Stopwatch.Frequency;
            if (ticks < long.MaxValue - now) deadline = now + (long)ticks;
        }
    }

    /// <summary>The limits this budget enforces.</summary>
    internal TemplateLimits Limits { get; }

    /// <summary>The budget of the render running on this thread, or <see langword="null"/>.</summary>
    internal static TemplateBudget Current => current;

    /// <summary>The limits in force on this thread: the running render's, or the defaults.</summary>
    internal static TemplateLimits CurrentLimits => current?.Limits ?? TemplateLimits.Default;

    /// <summary>
    /// Opens a budget for a render on this thread, or joins the one already open.
    /// </summary>
    /// <param name="limits">The limits, already validated; <see langword="null"/> means the defaults.</param>
    /// <param name="cancellationToken">The token that cancels the render.</param>
    /// <returns>The scope, which closes the budget when disposed if this call opened it.</returns>
    internal static Scope Open(TemplateLimits limits, CancellationToken cancellationToken)
    {
        if (current != null) return new Scope(null);

        TemplateBudget budget = new TemplateBudget(limits ?? TemplateLimits.Default, cancellationToken);
        current = budget;
        return new Scope(budget);
    }

    /// <summary>Charges one step and, every so often, looks at the clock and the token.</summary>
    /// <exception cref="ChatTemplateException">The step limit or the timeout was reached.</exception>
    /// <exception cref="OperationCanceledException">The render was cancelled.</exception>
    internal void Step()
    {
        steps++;
        if (steps > Limits.MaxRenderSteps)
        {
            throw Exceeded(nameof(TemplateLimits.MaxRenderSteps), Limits.MaxRenderSteps,
                "the template took more steps than that, which is what an endless or enormous loop does");
        }

        if (steps % CheckInterval == 0) CheckClock();
    }

    /// <summary>
    /// Looks at the clock and the token every so often, without charging a step: for the inner loops of
    /// filters and comparisons that can run long inside a single step.
    /// </summary>
    internal static void Poll()
    {
        TemplateBudget budget = current;
        if (budget == null) return;

        budget.polls++;
        if (budget.polls % CheckInterval == 0) budget.CheckClock();
    }

    /// <summary>Goes one level deeper.</summary>
    /// <exception cref="ChatTemplateException">The nesting limit was reached.</exception>
    internal void Enter()
    {
        EnsureStack();
        depth++;
        if (depth > Limits.MaxNestingDepth)
        {
            depth--;
            throw Exceeded(nameof(TemplateLimits.MaxNestingDepth), Limits.MaxNestingDepth,
                "the render nested deeper than that, which is what a macro or template recursing without end "
                + "does");
        }

        if (depth > DeepestLevel) DeepestLevel = depth;
    }

    /// <summary>How many steps the render has taken so far.</summary>
    internal long StepsTaken => steps;

    /// <summary>The deepest the render has nested so far.</summary>
    internal int DeepestLevel { get; private set; }

    /// <summary>
    /// Refuses to go deeper when the thread's stack is nearly used up, whatever the nesting limit says.
    /// </summary>
    /// <remarks>
    /// <see cref="TemplateLimits.MaxNestingDepth"/> is the policy, and its default fits an ordinary thread's
    /// stack with room to spare; this is the backstop for a thread with an unusually small stack, or a limit
    /// raised far past the default. A stack overflow cannot be caught - it ends the process - so the parsers,
    /// the renderers and the value walkers all ask this before every level of recursion.
    /// </remarks>
    /// <exception cref="ChatTemplateException">The stack is nearly exhausted.</exception>
    internal static void EnsureStack()
    {
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            throw new ChatTemplateException(
                "The chat template nests too deeply for the stack of the thread handling it, and was refused "
                + $"before the stack could overflow. Lower {nameof(TemplateLimits)}."
                + $"{nameof(TemplateLimits.MaxNestingDepth)} or use a thread with a larger stack.");
        }
    }

    /// <summary>Comes back up one level.</summary>
    internal void Exit() => depth--;

    /// <summary>Refuses a string that would be longer than the limit, before it is built where possible.</summary>
    /// <param name="length">The length the string has or would have.</param>
    /// <exception cref="ChatTemplateException">The length is over the limit.</exception>
    internal static void CheckString(double length)
    {
        int limit = CurrentLimits.MaxStringChars;
        if (length > limit)
        {
            throw Exceeded(nameof(TemplateLimits.MaxStringChars), limit,
                "the template tried to build a string of " + Describe(length) + " characters");
        }
    }

    /// <summary>Refuses a list or mapping that would hold more items than the limit.</summary>
    /// <param name="count">The number of items it has or would have.</param>
    /// <exception cref="ChatTemplateException">The count is over the limit.</exception>
    internal static void CheckItems(double count)
    {
        int limit = CurrentLimits.MaxCollectionItems;
        if (count > limit)
        {
            throw Exceeded(nameof(TemplateLimits.MaxCollectionItems), limit,
                "the template tried to build a collection of " + Describe(count) + " items");
        }
    }

    /// <summary>Refuses output that has grown past the limit.</summary>
    /// <param name="length">The length of the text being written.</param>
    /// <exception cref="ChatTemplateException">The length is over the limit.</exception>
    internal static void CheckOutput(long length)
    {
        int limit = CurrentLimits.MaxOutputChars;
        if (length > limit)
        {
            throw Exceeded(nameof(TemplateLimits.MaxOutputChars), limit,
                "the template wrote more text than that");
        }
    }

    /// <summary>Refuses a value nested deeper than the nesting limit.</summary>
    /// <param name="level">How deep the value has gone so far.</param>
    /// <exception cref="ChatTemplateException">The level is over the limit.</exception>
    internal static void CheckValueDepth(int level)
    {
        EnsureStack();
        int limit = CurrentLimits.MaxNestingDepth;
        if (level > limit)
        {
            throw Exceeded(nameof(TemplateLimits.MaxNestingDepth), limit,
                "a value is nested deeper than that, or contains itself");
        }
    }

    private void CheckClock()
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Stopwatch.GetTimestamp() > deadline)
        {
            throw new ChatTemplateException(
                $"The chat template exceeded {nameof(TemplateLimits)}.{nameof(TemplateLimits.RenderTimeout)} "
                + $"({Limits.RenderTimeout.GetValueOrDefault().TotalSeconds.ToString(CultureInfo.InvariantCulture)} s) "
                + "and the render was abandoned.");
        }
    }

    private static string Describe(double value)
        => value >= 1e15
            ? value.ToString("E3", CultureInfo.InvariantCulture)
            : ((long)value).ToString("N0", CultureInfo.InvariantCulture);

    private static ChatTemplateException Exceeded(string name, int limit, string detail)
        => new ChatTemplateException(
            $"The chat template exceeded {nameof(TemplateLimits)}.{name} "
            + $"({limit.ToString("N0", CultureInfo.InvariantCulture)}): {detail}.");

    /// <summary>Closes the budget it opened, if it opened one.</summary>
    internal readonly struct Scope : IDisposable
    {
        private readonly TemplateBudget opened;

        /// <summary>Wraps the budget this scope opened, or <see langword="null"/> when it joined one.</summary>
        /// <param name="opened">The budget.</param>
        internal Scope(TemplateBudget opened)
        {
            this.opened = opened;
        }

        /// <summary>Closes the budget if this scope opened it.</summary>
        public void Dispose()
        {
            if (opened != null && ReferenceEquals(current, opened)) current = null;
        }
    }
}
