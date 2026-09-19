namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// What one comparison operator answers for a pair of elements of one type. The result is always a boolean,
/// whatever the elements are.
/// </summary>
/// <typeparam name="T">The element type being compared.</typeparam>
internal interface IOnnxCompareOperation<T>
{
    /// <summary>Compares one pair of elements.</summary>
    /// <param name="left">The left element.</param>
    /// <param name="right">The right element.</param>
    /// <returns>What the comparison answers.</returns>
    static abstract bool Apply(T left, T right);
}
