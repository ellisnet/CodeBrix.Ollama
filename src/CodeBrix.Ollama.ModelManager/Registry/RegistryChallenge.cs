using System;

namespace CodeBrix.Ollama.ModelManager; //was previously: ollama/ollama server/images.go;

/// <summary>
/// The three values a registry puts in the <c>WWW-Authenticate</c> header of a 401 answer: the realm
/// that issues tokens, the service the token is for, and the scope it must cover.
/// </summary>
/// <remarks>
/// <see cref="Parse"/> is a port of Ollama's <c>parseRegistryChallenge</c> and <c>getValue</c>, kept
/// deliberately faithful down to the way it treats quotes: a value ends at the first double quote that
/// is followed by a comma or that sits at the end of the header, so a quote in the middle of a value
/// is kept, and a comma inside a quoted value does not end it.
/// </remarks>
internal sealed class RegistryChallenge
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RegistryChallenge"/> class.
    /// </summary>
    /// <param name="realm">The token endpoint the registry names.</param>
    /// <param name="service">The service name the token must be issued for.</param>
    /// <param name="scope">The scope the token must cover.</param>
    private RegistryChallenge(string realm, string service, string scope)
    {
        Realm = realm;
        Service = service;
        Scope = scope;
    }

    /// <summary>
    /// The token endpoint the registry names, or an empty string when the header did not carry one.
    /// </summary>
    public string Realm { get; }

    /// <summary>
    /// The service name the token must be issued for, or an empty string when the header did not
    /// carry one.
    /// </summary>
    public string Service { get; }

    /// <summary>
    /// The scope the token must cover, or an empty string when the header did not carry one.
    /// </summary>
    public string Scope { get; }

    /// <summary>
    /// Parses a <c>WWW-Authenticate</c> header value. A leading "Bearer " is dropped, then each of the
    /// three keys is looked up. Nothing here validates the header: a value that is not there comes back
    /// as an empty string, exactly as it does in Ollama.
    /// </summary>
    /// <param name="wwwAuthenticateHeader">
    /// The header value. <see langword="null"/> is treated as an empty header.
    /// </param>
    /// <returns>The parsed challenge, never <see langword="null"/>.</returns>
    public static RegistryChallenge Parse(string wwwAuthenticateHeader)
    {
        string header = wwwAuthenticateHeader ?? string.Empty;
        const string bearerPrefix = "Bearer ";
        if (header.StartsWith(bearerPrefix, StringComparison.Ordinal))
        {
            header = header.Substring(bearerPrefix.Length);
        }

        return new RegistryChallenge(
            GetValue(header, "realm"),
            GetValue(header, "service"),
            GetValue(header, "scope"));
    }

    /// <summary>
    /// Reads the value of one <c>key="value"</c> pair out of a challenge header.
    /// </summary>
    /// <param name="header">The header value, with any "Bearer " prefix already removed.</param>
    /// <param name="key">The key to read, without the "=" and without quotes.</param>
    /// <returns>The value, or an empty string when the key is not in the header.</returns>
    /// <remarks>
    /// The scan starts two characters after the key, which skips the "=" and the opening quote, and
    /// ends at a double quote that is either the last character of the header or is followed by a
    /// comma. Ollama's version indexes into the header without checking the bounds first; this one
    /// returns an empty string where that would have run off the end.
    /// </remarks>
    private static string GetValue(string header, string key)
    {
        int startIndex = header.IndexOf(key + "=", StringComparison.Ordinal);
        if (startIndex < 0)
        {
            return string.Empty;
        }

        // Move the index to the starting quote after the key.
        startIndex += key.Length + 2;
        if (startIndex > header.Length)
        {
            return string.Empty;
        }

        int endIndex = startIndex;
        while (endIndex < header.Length)
        {
            if (header[endIndex] == '"')
            {
                // If the next character isn't a comma, this quote is part of the value.
                if (endIndex + 1 < header.Length && header[endIndex + 1] != ',')
                {
                    endIndex++;
                    continue;
                }
                break;
            }
            endIndex++;
        }

        return header.Substring(startIndex, endIndex - startIndex);
    }
}
