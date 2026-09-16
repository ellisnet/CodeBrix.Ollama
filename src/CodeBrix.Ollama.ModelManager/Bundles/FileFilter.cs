using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Which files of a bundle are pulled: a set of include patterns and a set of exclude patterns over the
/// publisher's relative path. A path is kept when it matches at least one include pattern, or there are
/// no include patterns at all, and matches no exclude pattern.
/// </summary>
/// <remarks>
/// <para>
/// The patterns are globs over the whole relative path, matched case-sensitively and with forward
/// slashes as separators: <c>*</c> stands for any run of characters within one path segment,
/// <c>**</c> stands for any run of characters including separators, and <c>?</c> stands for one
/// character within a segment. A <c>**/</c> at the start of a pattern also matches no directory at all,
/// so <c>**/*.pt</c> matches <c>model.pt</c> as well as <c>weights/model.pt</c>.
/// </para>
/// <para>
/// One rule stands above the patterns: a file whose own name is <c>LICENSE</c>, <c>LICENSE.&lt;anything&gt;</c>,
/// <c>README</c> or <c>README.&lt;anything&gt;</c> is always kept, whatever the patterns say, because the
/// licence and the readme of a bundle are what a later <c>Show</c> reports the publisher's terms from.
/// The comparison of those names ignores case.
/// </para>
/// </remarks>
public sealed class FileFilter
{
    private static readonly FileFilter Everything = new FileFilter();

    private static readonly FileFilter TrainingArtifacts = new FileFilter(
        null,
        new[] { "logs/**", "**/*.tfevents*", "optimizer.pt", "**/optimizer*.pt" });

    private readonly string[] _includeGlobs;
    private readonly string[] _excludeGlobs;
    private readonly Regex[] _includePatterns;
    private readonly Regex[] _excludePatterns;

    /// <summary>
    /// Initializes a filter that keeps every file.
    /// </summary>
    public FileFilter()
        : this(null, null)
    {
    }

    /// <summary>
    /// Initializes a filter from its patterns.
    /// </summary>
    /// <param name="includeGlobs">
    /// The patterns a path must match one of, or <see langword="null"/> or empty for "every path".
    /// </param>
    /// <param name="excludeGlobs">
    /// The patterns a path must match none of, or <see langword="null"/> or empty for "nothing is
    /// excluded".
    /// </param>
    public FileFilter(IEnumerable<string> includeGlobs, IEnumerable<string> excludeGlobs)
    {
        _includeGlobs = ToArray(includeGlobs);
        _excludeGlobs = ToArray(excludeGlobs);
        _includePatterns = Compile(_includeGlobs);
        _excludePatterns = Compile(_excludeGlobs);
    }

    /// <summary>
    /// A filter that excludes nothing and includes everything.
    /// </summary>
    public static FileFilter Default
    {
        get { return Everything; }
    }

    /// <summary>
    /// A filter that leaves out what a training run wrote and an export never needs: the
    /// <c>logs</c> tree, every TensorBoard event file wherever it sits, and optimizer state
    /// (<c>optimizer.pt</c> and any <c>optimizer*.pt</c> below the root). Weights, configuration,
    /// tokenizer files and any ONNX files the publisher shipped are all kept.
    /// </summary>
    /// <remarks>
    /// This is opt-in. The filter a source is given when nothing else is said is
    /// <see cref="Default"/>, so a pull takes the publisher's repository as it stands unless the
    /// caller asks for less.
    /// </remarks>
    public static FileFilter ExcludeTrainingArtifacts
    {
        get { return TrainingArtifacts; }
    }

    /// <summary>The include patterns, in the order they were given.</summary>
    public IReadOnlyList<string> IncludeGlobs
    {
        get { return _includeGlobs; }
    }

    /// <summary>The exclude patterns, in the order they were given.</summary>
    public IReadOnlyList<string> ExcludeGlobs
    {
        get { return _excludeGlobs; }
    }

    /// <summary>Whether this filter has no patterns at all and therefore keeps every file.</summary>
    public bool KeepsEverything
    {
        get { return _includePatterns.Length == 0 && _excludePatterns.Length == 0; }
    }

    /// <summary>
    /// Returns a filter with more include patterns than this one.
    /// </summary>
    /// <param name="globs">The patterns to add.</param>
    /// <returns>The new filter. This one is unchanged.</returns>
    public FileFilter WithIncludes(params string[] globs)
    {
        var includes = new List<string>(_includeGlobs);
        includes.AddRange(ToArray(globs));
        return new FileFilter(includes, _excludeGlobs);
    }

    /// <summary>
    /// Returns a filter with more exclude patterns than this one.
    /// </summary>
    /// <param name="globs">The patterns to add.</param>
    /// <returns>The new filter. This one is unchanged.</returns>
    public FileFilter WithExcludes(params string[] globs)
    {
        var excludes = new List<string>(_excludeGlobs);
        excludes.AddRange(ToArray(globs));
        return new FileFilter(_includeGlobs, excludes);
    }

    /// <summary>
    /// Whether a relative path is kept.
    /// </summary>
    /// <param name="path">The publisher's relative path, with forward slashes.</param>
    /// <returns>
    /// <see langword="true"/> when the path is pulled, <see langword="false"/> when it is left behind.
    /// An empty path is never kept.
    /// </returns>
    public bool ShouldInclude(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        string normalized = path.Replace('\\', '/');
        if (IsAlwaysKept(normalized))
        {
            return true;
        }

        if (_includePatterns.Length > 0 && !MatchesAny(_includePatterns, normalized))
        {
            return false;
        }

        return !MatchesAny(_excludePatterns, normalized);
    }

    /// <summary>
    /// Whether a bundle file is kept.
    /// </summary>
    /// <param name="file">The file to test.</param>
    /// <returns><see langword="true"/> when the file is pulled.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="file"/> is <see langword="null"/>.</exception>
    public bool ShouldInclude(BundleFile file)
    {
        if (file == null)
        {
            throw new ArgumentNullException(nameof(file));
        }
        return ShouldInclude(file.Path);
    }

    /// <summary>
    /// Applies the filter to a list of files, keeping their order.
    /// </summary>
    /// <param name="files">The files to filter. <see langword="null"/> entries are dropped.</param>
    /// <returns>The files that are kept.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="files"/> is <see langword="null"/>.</exception>
    public IReadOnlyList<BundleFile> Apply(IEnumerable<BundleFile> files)
    {
        if (files == null)
        {
            throw new ArgumentNullException(nameof(files));
        }

        var kept = new List<BundleFile>();
        foreach (BundleFile file in files)
        {
            if (file != null && ShouldInclude(file.Path))
            {
                kept.Add(file);
            }
        }
        return kept;
    }

    /// <summary>
    /// Whether a path names a licence or a readme, which is kept however the patterns read.
    /// </summary>
    /// <param name="path">The relative path, with forward slashes.</param>
    /// <returns><see langword="true"/> when the file is one of those.</returns>
    private static bool IsAlwaysKept(string path)
    {
        int slash = path.LastIndexOf('/');
        string name = slash < 0 ? path : path.Substring(slash + 1);

        return IsNameOrExtensionOf(name, "LICENSE") || IsNameOrExtensionOf(name, "README");
    }

    /// <summary>
    /// Whether a file name is a stem exactly, or that stem with an extension.
    /// </summary>
    /// <param name="name">The file's own name.</param>
    /// <param name="stem">The stem to compare with, ignoring case.</param>
    /// <returns><see langword="true"/> when the name is the stem or the stem plus an extension.</returns>
    private static bool IsNameOrExtensionOf(string name, string stem)
    {
        if (string.Equals(name, stem, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return name.Length > stem.Length
            && name[stem.Length] == '.'
            && name.StartsWith(stem, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether a path matches any of a set of compiled patterns.
    /// </summary>
    /// <param name="patterns">The patterns.</param>
    /// <param name="path">The path to test.</param>
    /// <returns><see langword="true"/> when at least one pattern matches.</returns>
    private static bool MatchesAny(Regex[] patterns, string path)
    {
        foreach (Regex pattern in patterns)
        {
            if (pattern.IsMatch(path))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Copies the usable patterns of a sequence into an array.
    /// </summary>
    /// <param name="globs">The patterns, which may be <see langword="null"/>.</param>
    /// <returns>The patterns that are neither null nor blank.</returns>
    private static string[] ToArray(IEnumerable<string> globs)
    {
        if (globs == null)
        {
            return Array.Empty<string>();
        }

        var kept = new List<string>();
        foreach (string glob in globs)
        {
            if (!string.IsNullOrWhiteSpace(glob))
            {
                kept.Add(glob.Replace('\\', '/').Trim());
            }
        }
        return kept.ToArray();
    }

    /// <summary>
    /// Compiles every pattern to a regular expression anchored at both ends.
    /// </summary>
    /// <param name="globs">The patterns.</param>
    /// <returns>The compiled patterns.</returns>
    private static Regex[] Compile(string[] globs)
    {
        var patterns = new Regex[globs.Length];
        for (int index = 0; index < globs.Length; index++)
        {
            patterns[index] = new Regex(
                "^" + TranslateGlob(globs[index]) + "$",
                RegexOptions.CultureInvariant | RegexOptions.Singleline);
        }
        return patterns;
    }

    /// <summary>
    /// Turns one glob into the body of a regular expression: <c>**/</c> into "any run of segments or
    /// none", <c>**</c> into "anything", <c>*</c> into "anything within a segment", <c>?</c> into "one
    /// character within a segment", and everything else into itself.
    /// </summary>
    /// <param name="glob">The pattern.</param>
    /// <returns>The regular expression body.</returns>
    private static string TranslateGlob(string glob)
    {
        var builder = new StringBuilder(glob.Length * 4);
        int index = 0;

        while (index < glob.Length)
        {
            char character = glob[index];
            if (character == '*')
            {
                bool doubled = index + 1 < glob.Length && glob[index + 1] == '*';
                if (doubled)
                {
                    bool followedBySlash = index + 2 < glob.Length && glob[index + 2] == '/';
                    if (followedBySlash)
                    {
                        builder.Append("(?:.*/)?");
                        index += 3;
                    }
                    else
                    {
                        builder.Append(".*");
                        index += 2;
                    }
                }
                else
                {
                    builder.Append("[^/]*");
                    index++;
                }
                continue;
            }

            if (character == '?')
            {
                builder.Append("[^/]");
                index++;
                continue;
            }

            builder.Append(Regex.Escape(character.ToString()));
            index++;
        }

        return builder.ToString();
    }
}
