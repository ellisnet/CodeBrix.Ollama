using System.Globalization;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// One dimension of a graph input's or output's declared shape: either a fixed length, or a symbol whose
/// length is settled by the tensors a run is given.
/// </summary>
/// <remarks>
/// A decoder's graph is written in symbols - <c>batch</c>, <c>past_sequence_length</c>, <c>total_sequence</c> -
/// and the same symbol appearing twice means the two lengths agree at run time. The engine does not resolve
/// symbols before a run: every shape falls out of the tensors it is handed, and this is what the file says
/// about them so a driver can lay out its inputs.
/// </remarks>
public sealed class OnnxDimension
{
    private OnnxDimension(long length, string symbol)
    {
        Length = length;
        Symbol = symbol;
    }

    /// <summary>The fixed length, or -1 when the dimension is a symbol or was left unstated.</summary>
    public long Length { get; }

    /// <summary>The symbol's name, or <see langword="null"/> when the dimension is a fixed length.</summary>
    public string Symbol { get; }

    /// <summary>Whether the length is settled by the file rather than by the tensors a run is given.</summary>
    public bool IsFixed => Length >= 0;

    /// <summary>A dimension of a stated length.</summary>
    /// <param name="length">The length.</param>
    /// <returns>The dimension.</returns>
    internal static OnnxDimension Fixed(long length) => new OnnxDimension(length, null);

    /// <summary>A dimension the file leaves to the run, named or not.</summary>
    /// <param name="symbol">The symbol's name, or <see langword="null"/> when the file states none.</param>
    /// <returns>The dimension.</returns>
    internal static OnnxDimension Symbolic(string symbol) => new OnnxDimension(-1, symbol);

    /// <summary>Returns the length, or the symbol, or a question mark.</summary>
    /// <returns>A short description.</returns>
    public override string ToString()
    {
        if (IsFixed) return Length.ToString(CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(Symbol) ? "?" : Symbol;
    }
}
