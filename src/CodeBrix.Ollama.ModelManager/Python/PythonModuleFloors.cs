using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The lowest version of a Python module this library will run, for the modules whose older releases
/// carry a known hole that a hostile model can reach through the scripts this package ships. A module
/// with no entry here has no floor.
/// </summary>
/// <remarks>
/// The versions are compared the way Python packaging compares them, as far as a floor needs: the
/// release numbers decide, a local label such as <c>+cpu</c> is ignored, a post-release counts as its
/// release, and a pre-release or development build of the floor release itself (<c>2.6.0rc1</c>,
/// <c>2.6.0.dev20241112</c>) is BELOW the floor, because it may predate the fix.
/// </remarks>
internal static class PythonModuleFloors
{
    /// <summary>
    /// One floor: the version and the reason for it.
    /// </summary>
    internal sealed class Floor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Floor"/> class.
        /// </summary>
        /// <param name="minimum">The lowest acceptable release.</param>
        /// <param name="reason">Why, naming the advisory, as a phrase that ends a sentence.</param>
        internal Floor(Version minimum, string reason)
        {
            Minimum = minimum;
            Reason = reason;
        }

        /// <summary>The lowest acceptable release.</summary>
        internal Version Minimum { get; }

        /// <summary>Why, naming the advisory, as a phrase that ends a sentence.</summary>
        internal string Reason { get; }
    }

    /// <summary>The floors, keyed by the module name a caller asks for.</summary>
    private static readonly Dictionary<string, Floor> Floors = new Dictionary<string, Floor>(StringComparer.Ordinal)
    {
        ["torch"] = new Floor(new Version(2, 6, 0),
            "earlier releases let a crafted checkpoint run code through torch.load even with"
                + " weights_only=True (CVE-2025-32434)"),
        ["onnx"] = new Floor(new Version(1, 16, 0),
            "earlier releases follow external-data paths out of the model folder (CVE-2024-27318)"),
        ["transformers"] = new Floor(new Version(5, 10, 0),
            "earlier releases write a dict-form chat_template's names as file paths in save_pretrained,"
                + " outside the output folder (CVE-2026-9856)")
    };

    /// <summary>
    /// The floor for a module, or <see langword="null"/> when it has none.
    /// </summary>
    /// <param name="moduleName">The module name, as the caller asked for it.</param>
    /// <returns>The floor, or <see langword="null"/>.</returns>
    internal static Floor For(string moduleName)
        => moduleName != null && Floors.TryGetValue(moduleName, out Floor floor) ? floor : null;

    /// <summary>
    /// Renders a floor version as a person writes it: "2.6", "1.16", "5.10" - the trailing zero patch
    /// number dropped.
    /// </summary>
    /// <param name="version">The floor version.</param>
    /// <returns>The text.</returns>
    internal static string Describe(Version version)
        => version.Build > 0 ? version.ToString(3) : version.ToString(2);

    /// <summary>
    /// Whether a version a module reports is at least a floor.
    /// </summary>
    /// <param name="installed">What the module reports, such as <c>2.6.0+cpu</c>; may be <see langword="null"/>.</param>
    /// <param name="minimum">The floor.</param>
    /// <returns>
    /// <see langword="true"/> when the version is the floor release or later; <see langword="false"/> when
    /// it is earlier, is a pre-release or development build of the floor release, or cannot be read -
    /// a version that cannot be read cannot be shown to carry the fix.
    /// </returns>
    internal static bool Meets(string installed, Version minimum)
    {
        if (!TryParse(installed, out int epoch, out Version release, out bool isPreRelease))
        {
            return false;
        }

        if (epoch > 0)
        {
            //An epoch outranks every release number; none of the floors here has one.
            return true;
        }

        int compared = Pad(release).CompareTo(Pad(minimum));
        return compared > 0 || (compared == 0 && !isPreRelease);
    }

    /// <summary>
    /// Reads a Python package version: an optional <c>v</c>, an optional epoch (<c>1!</c>), the dotted
    /// release numbers, then whatever follows them - a local label after <c>+</c>, a post-release, or a
    /// pre-release or development marker.
    /// </summary>
    /// <param name="text">The version text.</param>
    /// <param name="epoch">The epoch, 0 when there is none.</param>
    /// <param name="release">The release numbers, at most four of them.</param>
    /// <param name="isPreRelease">Whether a pre-release or development marker follows the release.</param>
    /// <returns><see langword="true"/> when at least one release number was read.</returns>
    internal static bool TryParse(string text, out int epoch, out Version release, out bool isPreRelease)
    {
        epoch = 0;
        release = null;
        isPreRelease = false;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string value = text.Trim().ToLowerInvariant();
        if (value.StartsWith('v'))
        {
            value = value.Substring(1);
        }

        int bang = value.IndexOf('!');
        if (bang >= 0)
        {
            if (!int.TryParse(value.AsSpan(0, bang), NumberStyles.None, CultureInfo.InvariantCulture, out epoch))
            {
                return false;
            }
            value = value.Substring(bang + 1);
        }

        var numbers = new List<int>();
        int position = 0;
        while (position < value.Length && char.IsAsciiDigit(value[position]))
        {
            int start = position;
            while (position < value.Length && char.IsAsciiDigit(value[position]))
            {
                position++;
            }
            if (!int.TryParse(value.AsSpan(start, position - start), NumberStyles.None,
                    CultureInfo.InvariantCulture, out int number))
            {
                return false;
            }
            numbers.Add(number);

            //A dot continues the release only when a digit follows it; ".dev0" and ".post1" end it.
            if (position + 1 < value.Length && value[position] == '.' && char.IsAsciiDigit(value[position + 1]))
            {
                position++;
                continue;
            }
            break;
        }

        if (numbers.Count == 0)
        {
            return false;
        }

        while (numbers.Count < 2)
        {
            numbers.Add(0);
        }

        release = numbers.Count switch
        {
            2 => new Version(numbers[0], numbers[1]),
            3 => new Version(numbers[0], numbers[1], numbers[2]),
            _ => new Version(numbers[0], numbers[1], numbers[2], numbers[3])
        };

        string rest = value.Substring(position);
        int plus = rest.IndexOf('+');
        if (plus >= 0)
        {
            rest = rest.Substring(0, plus);
        }
        rest = rest.TrimStart('.', '-', '_');

        isPreRelease = rest.Length > 0
            && !rest.StartsWith("post", StringComparison.Ordinal)
            && !rest.StartsWith("rev", StringComparison.Ordinal)
            && !(rest[0] == 'r' && (rest.Length == 1 || char.IsAsciiDigit(rest[1])))
            && !char.IsAsciiDigit(rest[0]);

        return true;
    }

    /// <summary>
    /// Fills a version out to four numbers so that 2.6 and 2.6.0 compare equal.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <returns>The same version with every missing number as zero.</returns>
    private static Version Pad(Version version)
        => new Version(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
}
