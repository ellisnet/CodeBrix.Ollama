namespace CodeBrix.Ollama.ModelRunner;

/// <summary>What one element-by-element operator does to a single element of one type.</summary>
/// <typeparam name="T">The element type.</typeparam>
internal interface IOnnxUnaryOperation<T>
{
    /// <summary>Applies the operator to one element.</summary>
    /// <param name="value">The element.</param>
    /// <returns>The result.</returns>
    static abstract T Apply(T value);
}
