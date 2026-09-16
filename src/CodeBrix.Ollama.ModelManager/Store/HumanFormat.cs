using System;
using System.Globalization;

namespace CodeBrix.Ollama.ModelManager; //was previously: ollama/ollama format/format.go;

/// <summary>
/// The human-readable number formatting Ollama writes into a model's config layer. A parameter count
/// becomes "134.52M", "7.2B", "8B" or "512K", which is the string that ends up in
/// <see cref="ModelConfig.ModelType"/> and that <c>ollama list</c> prints as the parameter size.
/// </summary>
internal static class HumanFormat
{
    /// <summary>
    /// One thousand, the smallest magnitude that gets a suffix.
    /// </summary>
    public const ulong Thousand = 1000;

    /// <summary>
    /// One million.
    /// </summary>
    public const ulong Million = Thousand * 1000;

    /// <summary>
    /// One billion (a thousand million, as Ollama counts it).
    /// </summary>
    public const ulong Billion = Million * 1000;

    /// <summary>
    /// Formats a count the way Ollama's <c>format.HumanNumber</c> does: billions with no decimals when
    /// the result is whole and one decimal otherwise, millions with no decimals when whole and two
    /// otherwise, thousands with no decimals, and anything smaller as the plain number.
    /// </summary>
    /// <param name="value">The count, normally a model's parameter count.</param>
    /// <returns>The formatted string.</returns>
    public static string HumanNumber(ulong value)
    {
        if (value >= Billion)
        {
            double number = value / (double)Billion;
            return number == Math.Floor(number)
                ? number.ToString("F0", CultureInfo.InvariantCulture) + "B"
                : number.ToString("F1", CultureInfo.InvariantCulture) + "B";
        }

        if (value >= Million)
        {
            double number = value / (double)Million;
            return number == Math.Floor(number)
                ? number.ToString("F0", CultureInfo.InvariantCulture) + "M"
                : number.ToString("F2", CultureInfo.InvariantCulture) + "M";
        }

        if (value >= Thousand)
        {
            double number = value / (double)Thousand;
            return number.ToString("F0", CultureInfo.InvariantCulture) + "K";
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }
}
