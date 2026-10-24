namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Thrown when an address is refused before any request goes to it: a plain-HTTP address while
/// <see cref="ModelStoreOptions.AllowInsecureHttp"/> is not set, a redirect that leads somewhere a
/// download may not go, or an address whose scheme is neither http nor https.
/// </summary>
/// <remarks>
/// It is a <see cref="RegistryException"/> to every caller. It has a type of its own only so that the
/// download engine can tell a refusal, which no retry will change, from transport trouble, which a
/// retry may cure.
/// </remarks>
internal sealed class AddressRefusedException : RegistryException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AddressRefusedException"/> class.
    /// </summary>
    /// <param name="message">The message that says what was refused and why.</param>
    public AddressRefusedException(string message) : base(message, null, null)
    {
    }
}
