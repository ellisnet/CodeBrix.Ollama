namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Parses the boolean spellings that Go's <c>strconv.ParseBool</c> accepts. Ollama's parameter
/// typing runs every boolean PARAMETER value through that function, so a Modelfile written for
/// Ollama keeps its meaning here.
/// </summary>
internal static class GoBool
{
    /// <summary>
    /// Converts one of Go's accepted boolean spellings to a <see cref="bool"/>.
    /// </summary>
    /// <param name="value">The text to convert. The accepted spellings are 1, t, T, TRUE, true,
    /// True, 0, f, F, FALSE, false and False; everything else is rejected.</param>
    /// <param name="result">The converted value, or <see langword="false"/> when the text was not
    /// one of the accepted spellings.</param>
    /// <returns><see langword="true"/> when the text was converted.</returns>
    internal static bool TryParse(string value, out bool result)
    {
        switch (value)
        {
            case "1":
            case "t":
            case "T":
            case "TRUE":
            case "true":
            case "True":
                result = true;
                return true;
            case "0":
            case "f":
            case "F":
            case "FALSE":
            case "false":
            case "False":
                result = false;
                return true;
            default:
                result = false;
                return false;
        }
    }
}
