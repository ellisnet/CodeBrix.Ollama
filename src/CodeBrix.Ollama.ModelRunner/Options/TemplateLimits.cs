using System;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The budget a chat template is held to while it is parsed and rendered, in either dialect. A template is
/// code that arrives with a model file or a Modelfile, and these limits are what stop a hostile or broken
/// one from hanging a render, exhausting memory or overflowing the stack.
/// </summary>
/// <remarks>
/// <para>
/// Every default is far above what a real chat template needs - a long conversation with many tools renders
/// in a few thousand steps, nests a few dozen levels deep and produces as much text as the model's context
/// can hold - so an honest template never meets them. A template that does is refused with a
/// <see cref="ChatTemplateException"/> whose message names the limit, and nothing else is affected: the
/// model stays loaded and the next request renders normally.
/// </para>
/// <para>
/// Every count must be at least 1, and a <see cref="RenderTimeout"/> must be positive when it is set;
/// anything else is <see cref="ArgumentException"/> where the limits are first used.
/// </para>
/// </remarks>
public sealed class TemplateLimits
{
    /// <summary>
    /// The largest number of steps one render may take: every statement executed, every expression
    /// evaluated and every loop iteration is one step. Default 1,000,000. It is what ends a loop over
    /// <c>range(1000000000)</c> or a Go <c>{{range 1000000000000}}</c>.
    /// </summary>
    public int MaxRenderSteps { get; set; } = 1_000_000;

    /// <summary>
    /// The largest number of characters one render may produce, counted in the text being written, including
    /// text captured by a <c>{% set %}</c> or <c>{% filter %}</c> block or a macro call. Default 8,000,000,
    /// which is more text than any model context holds.
    /// </summary>
    public int MaxOutputChars { get; set; } = 8_000_000;

    /// <summary>
    /// The longest string a render may build, in characters: string repetition, padding and format widths,
    /// <c>replace</c>, <c>join</c>, concatenation and every function result are held to it, and the
    /// width-driven ones are checked BEFORE anything is allocated. Default 8,000,000.
    /// </summary>
    public int MaxStringChars { get; set; } = 8_000_000;

    /// <summary>
    /// The largest list or mapping a render may build: <c>range()</c>, <c>batch</c>, <c>slice</c>,
    /// <c>extend</c>, list concatenation and the rest. Default 1,000,000.
    /// </summary>
    public int MaxCollectionItems { get; set; } = 1_000_000;

    /// <summary>
    /// How deeply a template may nest. It is checked three ways: while parsing (blocks inside blocks,
    /// parentheses inside parentheses and long operator, filter and attribute chains), while rendering
    /// (statements and expressions being evaluated, counted through every macro or <c>{{template}}</c> call,
    /// so a recursive macro is stopped too), and in the values a template builds (a list nested in a list,
    /// or one that contains itself). Default 256, which keeps the deepest permitted template well inside a
    /// thread's stack; raising it far above that risks a stack overflow, which ends the process.
    /// </summary>
    public int MaxNestingDepth { get; set; } = 256;

    /// <summary>
    /// How long one render may run before it is abandoned, or <see langword="null"/> for no time limit.
    /// Default 10 seconds. It is measured on the render's own clock and checked as the render goes, so it
    /// costs no thread and no timer; a render ends at the next check after the time is up.
    /// </summary>
    public TimeSpan? RenderTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The limits a template gets when none are given. Never handed out, so never changed.</summary>
    internal static TemplateLimits Default { get; } = new TemplateLimits();

    /// <summary>Makes an independent copy, so that a caller's later edits cannot change a loaded model.</summary>
    /// <returns>The copy.</returns>
    internal TemplateLimits Copy() => new TemplateLimits
    {
        MaxRenderSteps = MaxRenderSteps,
        MaxOutputChars = MaxOutputChars,
        MaxStringChars = MaxStringChars,
        MaxCollectionItems = MaxCollectionItems,
        MaxNestingDepth = MaxNestingDepth,
        RenderTimeout = RenderTimeout,
    };

    /// <summary>Checks every limit and returns an independent copy of them.</summary>
    /// <param name="limits">The limits, or <see langword="null"/> for the defaults.</param>
    /// <param name="parameterName">The parameter name to report.</param>
    /// <returns>The validated copy.</returns>
    /// <exception cref="ArgumentException">A count is below 1, or the timeout is zero or negative.</exception>
    internal static TemplateLimits ValidatedCopy(TemplateLimits limits, string parameterName)
    {
        if (limits == null) return Default;

        Require(limits.MaxRenderSteps, nameof(MaxRenderSteps), parameterName);
        Require(limits.MaxOutputChars, nameof(MaxOutputChars), parameterName);
        Require(limits.MaxStringChars, nameof(MaxStringChars), parameterName);
        Require(limits.MaxCollectionItems, nameof(MaxCollectionItems), parameterName);
        Require(limits.MaxNestingDepth, nameof(MaxNestingDepth), parameterName);

        if (limits.RenderTimeout.HasValue && limits.RenderTimeout.Value <= TimeSpan.Zero)
        {
            throw new ArgumentException(
                $"{nameof(TemplateLimits)}.{nameof(RenderTimeout)} must be positive; leave it null for no time "
                + "limit.", parameterName);
        }

        return limits.Copy();
    }

    private static void Require(int value, string name, string parameterName)
    {
        if (value < 1)
        {
            throw new ArgumentException(
                $"{nameof(TemplateLimits)}.{name} must be at least 1.", parameterName);
        }
    }
}
