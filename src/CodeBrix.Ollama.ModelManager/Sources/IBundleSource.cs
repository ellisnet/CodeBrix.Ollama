using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Something that can say what a bundle holds before anything is downloaded. Each implementation knows
/// one service - the Hugging Face Hub, a plain list of addresses - and holds whatever that service
/// needs to be asked, so a caller that has a source needs nothing else to list it.
/// </summary>
public interface IBundleSource
{
    /// <summary>
    /// Asks the source what the bundle holds.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the requests the listing makes.</param>
    /// <returns>
    /// The files, already filtered, with the revision the listing was taken at and whatever the source
    /// states about the licence.
    /// </returns>
    /// <exception cref="RegistryException">
    /// The service answered an error status, answered something that cannot be read, or refused the
    /// request because the repository needs credentials.
    /// </exception>
    Task<BundleListing> ListAsync(CancellationToken cancellationToken = default);
}
