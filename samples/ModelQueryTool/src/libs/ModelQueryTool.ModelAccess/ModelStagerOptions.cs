using System;
using System.IO;
using ModelQueryTool.ModelAccess.Models;

namespace ModelQueryTool.ModelAccess;

/// <summary>
/// What a stager works on: which model, and which folder of its own it keeps the files in. Both have a
/// default, so an application that wants the ordinary behaviour creates a stager with no options at all.
/// </summary>
public sealed class ModelStagerOptions
{
    /// <summary>
    /// The folder inside the root that holds the store. Its layout is the one the store library writes:
    /// <c>blobs/</c> beside <c>manifests/</c>.
    /// </summary>
    public const string StoreFolderName = "models";

    /// <summary>The folder the application keeps everything of its own in, under local application data.</summary>
    public const string ApplicationFolderName = "ModelQueryTool";

    /// <summary>
    /// Gets or sets the model to stage. <see langword="null"/> means <see cref="KnownModels.Qwen35"/>,
    /// which is the only model the application ever asks for.
    /// </summary>
    public ModelDescriptor Model { get; set; } = KnownModels.Qwen35;

    /// <summary>
    /// Gets or sets the folder the application owns. <see langword="null"/> means
    /// <see cref="ResolveDefaultRootDirectory"/>. Everything this library writes lives inside it, and
    /// removing the model removes the folder itself.
    /// </summary>
    public string RootDirectory { get; set; }

    /// <summary>
    /// The folder the application owns when no other one is named:
    /// <c>&lt;local application data&gt;/ModelQueryTool</c>.
    /// </summary>
    /// <remarks>
    /// Local application data is resolved WITHOUT verifying that it exists, because the case this
    /// library was written for is the machine where it does not exist yet - and with the ordinary
    /// option .NET answers that case with an empty string.
    /// </remarks>
    /// <returns>The absolute path of the default root folder.</returns>
    public static string ResolveDefaultRootDirectory()
    {
        string baseDirectory = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);

        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            string profile = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile,
                Environment.SpecialFolderOption.DoNotVerify);

            baseDirectory = string.IsNullOrWhiteSpace(profile)
                ? Directory.GetCurrentDirectory()
                : Path.Combine(profile, ".local", "share");
        }

        return Path.GetFullPath(Path.Combine(baseDirectory, ApplicationFolderName));
    }
}
