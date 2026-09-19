using System;
using System.Threading;
using System.Threading.Tasks;
using ModelQueryTool.ModelAccess.Models;

namespace ModelQueryTool.ModelAccess;

/// <summary>
/// The whole of what the application does about obtaining a model: find out whether it is there, put it
/// there, and take it away again so completely that the folder it lived in is gone.
/// </summary>
/// <remarks>
/// <para>
/// The store is the application's own folder and nothing else ever writes to it, so "the model is
/// staged" is a question that can be answered from the files alone. Nothing is remembered between runs.
/// </para>
/// <para>
/// Staging and removing are serialized inside one stager: two downloads of one model are not
/// coordinated by the store underneath, so only one of them is ever let through at a time.
/// </para>
/// </remarks>
public interface IModelStager : IDisposable
{
    /// <summary>Gets the model this stager obtains.</summary>
    ModelDescriptor Model { get; }

    /// <summary>Gets the folder the application owns, which removal deletes when it is left empty.</summary>
    string RootDirectory { get; }

    /// <summary>Gets the store folder inside the root that the model's files live in.</summary>
    string StoreDirectory { get; }

    /// <summary>
    /// Looks at the store and says how much of the model is there. It creates nothing - not a file, not
    /// a folder - so it is safe to call on a machine that has never staged anything.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the check.</param>
    /// <returns>What was found.</returns>
    /// <exception cref="ObjectDisposedException">The stager has been disposed.</exception>
    Task<ModelStagingStatus> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes the model ready: downloads it when it is not there, resumes an interrupted download, and
    /// repairs a damaged one. A model that is already ready costs no request at all.
    /// </summary>
    /// <param name="progress">
    /// Where to report the one fraction across the whole model, or <see langword="null"/> to report
    /// nothing. The last report of a successful run is one hundred percent.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that cancels the download. Cancelling leaves what has arrived in place, so a later call
    /// carries on from there.
    /// </param>
    /// <returns>What the store holds afterwards, which is a ready model.</returns>
    /// <exception cref="ModelAccessException">The model could not be obtained.</exception>
    /// <exception cref="OperationCanceledException">The download was cancelled.</exception>
    /// <exception cref="ObjectDisposedException">The stager has been disposed.</exception>
    Task<ModelStagingStatus> StageAsync(
        IProgress<ModelStagingProgress> progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the model away again: the manifest, the files it named, the folders they lived in and the
    /// application's own folder, each one only when nothing else is left in it. Removing what is not
    /// there is a quiet success that creates nothing.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the removal.</param>
    /// <returns>What the store holds afterwards, which is nothing of this model.</returns>
    /// <exception cref="ModelAccessException">The model could not be removed.</exception>
    /// <exception cref="OperationCanceledException">The removal was cancelled.</exception>
    /// <exception cref="ObjectDisposedException">The stager has been disposed.</exception>
    Task<ModelStagingStatus> RemoveAsync(CancellationToken cancellationToken = default);
}
