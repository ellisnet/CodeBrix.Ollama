namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// What one element-by-element operator does to a pair of elements of one type.
/// </summary>
/// <remarks>
/// The kernel class itself implements this, once per element type it supports, and hands ITSELF to the shared
/// broadcasting walk as a generic argument. Because the member is static and abstract, the compiler knows at
/// the call site which operator is meant and the arithmetic is inlined into the loop: one implementation of
/// numpy broadcasting serves every operator without a delegate call per element.
/// </remarks>
/// <typeparam name="T">The element type.</typeparam>
internal interface IOnnxBinaryOperation<T>
{
    /// <summary>Applies the operator to one pair of elements.</summary>
    /// <param name="left">The left element.</param>
    /// <param name="right">The right element.</param>
    /// <returns>The result.</returns>
    static abstract T Apply(T left, T right);
}
