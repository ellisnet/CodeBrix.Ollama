// Ported from Ollama (https://github.com/ollama/ollama), MIT License, Copyright (c) Ollama. Source: types/model/name.go at commit a43fad18.
using System;
using System.IO;
using System.Text;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// A structured representation of a model name string, in the form
/// <c>[scheme://][host/][namespace/]model[:tag]</c>.
/// </summary>
/// <remarks>
/// <para>
/// A parsed name is not guaranteed to be usable. Parsing never throws; it mirrors Ollama, which
/// returns a value whose parts may be absent or nonsense. Check <see cref="IsValid"/> before using
/// the result, or use <see cref="TryParse"/>.
/// </para>
/// <para>
/// The grammar each part must match, as Ollama defines it:
/// </para>
/// <list type="bullet">
///   <item><description>host: <c>{ alphanum | "_" } { alphanum | "-" | "_" | "." | ":" }*</c>, length 1 to 350.</description></item>
///   <item><description>namespace: <c>{ alphanum | "_" } { alphanum | "-" | "_" }*</c>, length 1 to 80.</description></item>
///   <item><description>model: <c>{ alphanum | "_" } { alphanum | "-" | "_" | "." }*</c>, length 1 to 80.</description></item>
///   <item><description>tag: <c>{ alphanum | "_" } { alphanum | "-" | "_" | "." }*</c>, length 1 to 80.</description></item>
/// </list>
/// <para>
/// An absent part is <see langword="null"/>. A part that the presence of a separator promised but
/// that turned out to be empty holds <see cref="MissingPart"/>, which never passes validation.
/// </para>
/// </remarks>
public readonly struct ModelName : IEquatable<ModelName>
{
    /// <summary>
    /// The value a part holds when a separator promised it but nothing was there. It is deliberately
    /// not a valid part, so a name that contains it never reports <see cref="IsValid"/>.
    /// </summary>
    public const string MissingPart = "!MISSING!";

    /// <summary>
    /// The host a name without a host part is completed with: "registry.ollama.ai".
    /// </summary>
    public const string DefaultHost = "registry.ollama.ai";

    /// <summary>
    /// The namespace a name without a namespace part is completed with: "library".
    /// </summary>
    public const string DefaultNamespace = "library";

    /// <summary>
    /// The tag a name without a tag part is completed with: "latest".
    /// </summary>
    public const string DefaultTag = "latest";

    /// <summary>
    /// The protocol scheme a name without a scheme part is completed with: "https".
    /// </summary>
    public const string DefaultProtocolScheme = "https";

    private const int MaxHostLength = 350;
    private const int MaxPartLength = 80;

    /// <summary>
    /// Identifies which grammar a part is checked against.
    /// </summary>
    private enum PartKind
    {
        Host,
        Namespace,
        Model,
        Tag,
        Digest,
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelName"/> struct from its individual parts.
    /// An empty string is stored as <see langword="null"/>, so an absent part is always
    /// <see langword="null"/>.
    /// </summary>
    /// <param name="host">The registry host, or <see langword="null"/> when absent.</param>
    /// <param name="namespace">The namespace, or <see langword="null"/> when absent.</param>
    /// <param name="model">The model, or <see langword="null"/> when absent.</param>
    /// <param name="tag">The tag, or <see langword="null"/> when absent.</param>
    /// <param name="protocolScheme">The protocol scheme, or <see langword="null"/> when absent.</param>
    public ModelName(string host, string @namespace, string model, string tag, string protocolScheme)
    {
        Host = NullIfEmpty(host);
        Namespace = NullIfEmpty(@namespace);
        Model = NullIfEmpty(model);
        Tag = NullIfEmpty(tag);
        ProtocolScheme = NullIfEmpty(protocolScheme);
    }

    /// <summary>The registry host, or <see langword="null"/> when absent.</summary>
    public string Host { get; }

    /// <summary>The namespace, or <see langword="null"/> when absent.</summary>
    public string Namespace { get; }

    /// <summary>The model, or <see langword="null"/> when absent.</summary>
    public string Model { get; }

    /// <summary>The tag, or <see langword="null"/> when absent.</summary>
    public string Tag { get; }

    /// <summary>The protocol scheme, or <see langword="null"/> when absent.</summary>
    public string ProtocolScheme { get; }

    /// <summary>
    /// A name carrying the default host, namespace, tag and protocol scheme. The model part is
    /// absent. This is what <see cref="Parse(string)"/> completes a parsed name with.
    /// </summary>
    public static ModelName Default
    {
        get { return new ModelName(DefaultHost, DefaultNamespace, null, DefaultTag, DefaultProtocolScheme); }
    }

    /// <summary>
    /// Builds a name that carries nothing but defaults, for use as the second argument of
    /// <see cref="Parse(string, ModelName)"/> or <see cref="Merge"/>. The model part is absent.
    /// </summary>
    /// <param name="host">The default host.</param>
    /// <param name="namespace">The default namespace.</param>
    /// <param name="tag">The default tag.</param>
    /// <param name="protocolScheme">The default protocol scheme.</param>
    /// <returns>A name holding the supplied defaults.</returns>
    public static ModelName CreateDefaults(string host, string @namespace, string tag, string protocolScheme)
    {
        return new ModelName(host, @namespace, null, tag, protocolScheme);
    }

    /// <summary>
    /// Parses a name string and completes the absent host, namespace, tag and scheme parts from
    /// <see cref="Default"/>.
    /// </summary>
    /// <param name="name">The name string.</param>
    /// <returns>
    /// The parsed name, which is not guaranteed to be valid. This method never throws; check
    /// <see cref="IsValid"/> on the result.
    /// </returns>
    public static ModelName Parse(string name)
    {
        return Merge(ParseBare(name), Default);
    }

    /// <summary>
    /// Parses a name string and completes the absent host, namespace, tag and scheme parts from the
    /// supplied defaults.
    /// </summary>
    /// <param name="name">The name string.</param>
    /// <param name="defaults">The name whose parts fill in the ones the parsed name is missing.</param>
    /// <returns>
    /// The parsed name, which is not guaranteed to be valid. This method never throws; check
    /// <see cref="IsValid"/> on the result.
    /// </returns>
    public static ModelName Parse(string name, ModelName defaults)
    {
        return Merge(ParseBare(name), defaults);
    }

    /// <summary>
    /// Parses a name string with the default parts and reports whether the result is valid.
    /// </summary>
    /// <param name="name">The name string.</param>
    /// <param name="result">The parsed name. Valid only when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the parsed name is valid.</returns>
    public static bool TryParse(string name, out ModelName result)
    {
        result = Parse(name);
        return result.IsValid;
    }

    /// <summary>
    /// Parses a name string without completing any part from the defaults.
    /// </summary>
    /// <param name="name">The name string. <see langword="null"/> is treated as empty.</param>
    /// <returns>
    /// The parsed name, which is not guaranteed to be valid. A digest suffix is not recognised as a
    /// part of its own: as in Ollama, an "@digest" suffix stays attached to whichever part it
    /// follows, which makes that part invalid.
    /// </returns>
    public static ModelName ParseBare(string name)
    {
        string remainder = name ?? string.Empty;
        string tag = null;

        // "/" is an illegal tag character, so the last ":" only starts a tag when it comes after the
        // last "/"; otherwise it belongs to the host part, as in "host:port/namespace/model".
        if (remainder.LastIndexOf(':') > remainder.LastIndexOf('/'))
        {
            CutPromised(remainder, ':', out remainder, out tag);
        }

        if (!CutPromised(remainder, '/', out string beforeModel, out string model))
        {
            return new ModelName(null, null, remainder, tag, null);
        }
        remainder = beforeModel;

        if (!CutPromised(remainder, '/', out string beforeNamespace, out string @namespace))
        {
            return new ModelName(null, remainder, model, tag, null);
        }
        remainder = beforeNamespace;

        string protocolScheme = null;
        string host = remainder;
        int schemeEnd = remainder.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd >= 0)
        {
            protocolScheme = remainder.Substring(0, schemeEnd);
            host = remainder.Substring(schemeEnd + 3);
        }

        return new ModelName(host, @namespace, model, tag, protocolScheme);
    }

    /// <summary>
    /// Parses a four-part relative path of the form <c>{host}/{namespace}/{model}/{tag}</c>, the
    /// layout the manifests directory of a model store uses.
    /// </summary>
    /// <param name="relativePath">
    /// The relative path. Both <see cref="Path.DirectorySeparatorChar"/> and "/" are accepted as
    /// separators. <see langword="null"/> is treated as empty.
    /// </param>
    /// <returns>
    /// The parsed name, or the default <see cref="ModelName"/> when the path does not have exactly
    /// four parts or the resulting name is not fully qualified.
    /// </returns>
    public static ModelName ParseFromRelativePath(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return default;
        }

        string[] parts = relativePath.Split(Path.DirectorySeparatorChar == '/'
            ? new[] { '/' }
            : new[] { Path.DirectorySeparatorChar, '/' });
        if (parts.Length != 4)
        {
            return default;
        }

        var name = new ModelName(parts[0], parts[1], parts[2], parts[3], null);
        return name.IsFullyQualified ? name : default;
    }

    /// <summary>
    /// Merges the host, namespace, tag and protocol scheme of two names, preferring the parts of
    /// <paramref name="a"/> that are present. The model part is taken from <paramref name="a"/>
    /// unchanged.
    /// </summary>
    /// <param name="a">The name whose present parts win.</param>
    /// <param name="b">The name supplying the parts <paramref name="a"/> is missing.</param>
    /// <returns>The merged name.</returns>
    public static ModelName Merge(ModelName a, ModelName b)
    {
        return new ModelName(
            a.Host ?? b.Host,
            a.Namespace ?? b.Namespace,
            a.Model,
            a.Tag ?? b.Tag,
            a.ProtocolScheme ?? b.ProtocolScheme);
    }

    /// <summary>
    /// Reports whether the provided string is a valid namespace.
    /// </summary>
    /// <param name="value">The candidate namespace.</param>
    /// <returns><see langword="true"/> when the string is a valid namespace.</returns>
    public static bool IsValidNamespace(string value)
    {
        return IsValidPart(PartKind.Namespace, value);
    }

    /// <summary>
    /// Whether every part of the name is present and valid. Ollama removed the digest check from
    /// this test and plans to reinstate it later, so this is the same test as
    /// <see cref="IsFullyQualified"/>.
    /// </summary>
    public bool IsValid
    {
        get { return IsFullyQualified; }
    }

    /// <summary>
    /// Whether the host, namespace, model and tag parts are all present and valid.
    /// </summary>
    public bool IsFullyQualified
    {
        get
        {
            return IsValidPart(PartKind.Host, Host)
                && IsValidPart(PartKind.Namespace, Namespace)
                && IsValidPart(PartKind.Model, Model)
                && IsValidPart(PartKind.Tag, Tag);
        }
    }

    /// <summary>
    /// The canonical relative path of the name, with every part from host to tag as a directory:
    /// <c>{host}/{namespace}/{model}/{tag}</c>, joined with
    /// <see cref="Path.DirectorySeparatorChar"/>.
    /// </summary>
    /// <returns>The relative path.</returns>
    /// <exception cref="InvalidOperationException">The name is not fully qualified.</exception>
    public string ToRelativePath()
    {
        if (!IsFullyQualified)
        {
            throw new InvalidOperationException(
                "The relative path of a model name that is not fully qualified cannot be built.");
        }
        return string.Join(Path.DirectorySeparatorChar, Host, Namespace, Model, Tag);
    }

    /// <summary>
    /// The name string, in the form <c>[host/][namespace/]model[:tag]</c>. Parts that are absent are
    /// left out along with their separator.
    /// </summary>
    /// <returns>The name string.</returns>
    public override string ToString()
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrEmpty(Host))
        {
            builder.Append(Host);
            builder.Append('/');
        }
        if (!string.IsNullOrEmpty(Namespace))
        {
            builder.Append(Namespace);
            builder.Append('/');
        }
        builder.Append(Model);
        if (!string.IsNullOrEmpty(Tag))
        {
            builder.Append(':');
            builder.Append(Tag);
        }
        return builder.ToString();
    }

    /// <summary>
    /// The shortest string that still identifies the name: the host is left out when it is the
    /// default host, and the namespace as well when it is the default namespace. The model and tag
    /// are always included.
    /// </summary>
    /// <returns>The shortened name string.</returns>
    public string DisplayShortest()
    {
        var builder = new StringBuilder();
        if (!string.Equals(Host, DefaultHost, StringComparison.OrdinalIgnoreCase))
        {
            builder.Append(Host);
            builder.Append('/');
            builder.Append(Namespace);
            builder.Append('/');
        }
        else if (!string.Equals(Namespace, DefaultNamespace, StringComparison.OrdinalIgnoreCase))
        {
            builder.Append(Namespace);
            builder.Append('/');
        }

        builder.Append(Model);
        builder.Append(':');
        builder.Append(Tag);
        return builder.ToString();
    }

    /// <summary>
    /// The namespace and model joined by "/".
    /// </summary>
    /// <returns>The <c>namespace/model</c> string.</returns>
    public string DisplayNamespaceModel()
    {
        var builder = new StringBuilder();
        builder.Append(Namespace);
        builder.Append('/');
        builder.Append(Model);
        return builder.ToString();
    }

    /// <summary>
    /// The base address of the registry the name belongs to, built from the protocol scheme and the
    /// host.
    /// </summary>
    /// <returns>The base address.</returns>
    /// <exception cref="UriFormatException">
    /// The scheme and host do not form an absolute address. Unlike Ollama, which assembles the parts
    /// without checking them, <see cref="Uri"/> validates what it is given.
    /// </exception>
    public Uri BaseUrl()
    {
        return new Uri(ProtocolScheme + "://" + Host);
    }

    /// <summary>
    /// Whether this name and another have the same host, namespace, model and tag, ignoring case.
    /// The protocol scheme takes no part in the comparison.
    /// </summary>
    /// <param name="other">The name to compare with.</param>
    /// <returns><see langword="true"/> when the two names match, ignoring case.</returns>
    public bool EqualsIgnoreCase(ModelName other)
    {
        return string.Equals(Host, other.Host, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Namespace, other.Namespace, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Model, other.Model, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Tag, other.Tag, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether this name and another have the same host, namespace, model and tag, compared as
    /// ordinal strings. The protocol scheme takes no part in the comparison.
    /// </summary>
    /// <param name="other">The name to compare with.</param>
    /// <returns><see langword="true"/> when the two names match.</returns>
    public bool Equals(ModelName other)
    {
        return string.Equals(Host, other.Host, StringComparison.Ordinal)
            && string.Equals(Namespace, other.Namespace, StringComparison.Ordinal)
            && string.Equals(Model, other.Model, StringComparison.Ordinal)
            && string.Equals(Tag, other.Tag, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the supplied object is a <see cref="ModelName"/> equal to this one.
    /// </summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> when the object is an equal name.</returns>
    public override bool Equals(object obj)
    {
        return obj is ModelName other && Equals(other);
    }

    /// <summary>
    /// A hash code built from the host, namespace, model and tag.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(
            Host == null ? 0 : StringComparer.Ordinal.GetHashCode(Host),
            Namespace == null ? 0 : StringComparer.Ordinal.GetHashCode(Namespace),
            Model == null ? 0 : StringComparer.Ordinal.GetHashCode(Model),
            Tag == null ? 0 : StringComparer.Ordinal.GetHashCode(Tag));
    }

    /// <summary>
    /// Whether two names are equal.
    /// </summary>
    /// <param name="left">The first name.</param>
    /// <param name="right">The second name.</param>
    /// <returns><see langword="true"/> when the two names are equal.</returns>
    public static bool operator ==(ModelName left, ModelName right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Whether two names are different.
    /// </summary>
    /// <param name="left">The first name.</param>
    /// <param name="right">The second name.</param>
    /// <returns><see langword="true"/> when the two names are not equal.</returns>
    public static bool operator !=(ModelName left, ModelName right)
    {
        return !left.Equals(right);
    }

    private static string NullIfEmpty(string value)
    {
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static bool IsValidLength(PartKind kind, string value)
    {
        int max = kind == PartKind.Host ? MaxHostLength : MaxPartLength;
        return value.Length >= 1 && value.Length <= max;
    }

    private static bool IsValidPart(PartKind kind, string value)
    {
        if (value == null || !IsValidLength(kind, value))
        {
            return false;
        }

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (i == 0)
            {
                if (!IsAlphanumericOrUnderscore(c))
                {
                    return false;
                }
                continue;
            }

            switch (c)
            {
                case '_':
                case '-':
                    break;
                case '.':
                    if (kind == PartKind.Namespace)
                    {
                        return false;
                    }
                    break;
                case ':':
                    if (kind != PartKind.Host && kind != PartKind.Digest)
                    {
                        return false;
                    }
                    break;
                default:
                    if (!IsAlphanumericOrUnderscore(c))
                    {
                        return false;
                    }
                    break;
            }
        }

        return true;
    }

    private static bool IsAlphanumericOrUnderscore(char c)
    {
        return (c >= 'A' && c <= 'Z')
            || (c >= 'a' && c <= 'z')
            || (c >= '0' && c <= '9')
            || c == '_';
    }

    private static bool CutLast(string s, char separator, out string before, out string after)
    {
        int index = s.LastIndexOf(separator);
        if (index >= 0)
        {
            before = s.Substring(0, index);
            after = s.Substring(index + 1);
            return true;
        }
        before = s;
        after = string.Empty;
        return false;
    }

    /// <summary>
    /// Cuts the string at the last occurrence of the separator. When the separator is found, an
    /// empty side is returned as <see cref="MissingPart"/>, which never passes validation.
    /// </summary>
    private static bool CutPromised(string s, char separator, out string before, out string after)
    {
        if (!CutLast(s, separator, out before, out after))
        {
            return false;
        }
        if (before.Length == 0)
        {
            before = MissingPart;
        }
        if (after.Length == 0)
        {
            after = MissingPart;
        }
        return true;
    }
}
