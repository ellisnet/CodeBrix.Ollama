using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelManager; //was previously: gguf-py/gguf/metadata.py@b10221;

/// <summary>
/// The <c>general.*</c> metadata a conversion records: who made the model, what it is called, what size it is
/// and what licence it carries.
/// </summary>
/// <remarks>
/// <para>
/// The values come from three places, in this order: the model card's YAML front matter, the
/// <c>_name_or_path</c> in <c>config.json</c> when it looks like a model identifier rather than a path, and
/// finally the model identifier the caller supplied - which is the checkpoint directory's own name unless
/// something better is known. The first place to fill a field wins.
/// </para>
/// <para>
/// The base models and datasets a card may name are NOT read. They need nested structures that the front-matter
/// subset in <see cref="ModelCard"/> does not cover, so the keys they would write are not written, and a card
/// carrying them converts without them.
/// </para>
/// </remarks>
internal sealed class ModelMetadata
{
    /// <summary>The model's display name.</summary>
    internal string Name { get; set; }

    /// <summary>Who wrote the model.</summary>
    internal string Author { get; set; }

    /// <summary>The model's version.</summary>
    internal string Version { get; set; }

    /// <summary>The organization that published the model.</summary>
    internal string Organization { get; set; }

    /// <summary>The finetune the model is.</summary>
    internal string Finetune { get; set; }

    /// <summary>The name the model shares with its siblings.</summary>
    internal string Basename { get; set; }

    /// <summary>A sentence about the model.</summary>
    internal string Description { get; set; }

    /// <summary>Who quantized the model.</summary>
    internal string QuantizedBy { get; set; }

    /// <summary>The parameter-count class, for example <c>7B</c>.</summary>
    internal string SizeLabel { get; set; }

    /// <summary>The licence identifier.</summary>
    internal string License { get; set; }

    /// <summary>The licence's name, when it has one beyond the identifier.</summary>
    internal string LicenseName { get; set; }

    /// <summary>A link to the licence.</summary>
    internal string LicenseLink { get; set; }

    /// <summary>The source model's page.</summary>
    internal string SourceUrl { get; set; }

    /// <summary>The source model's digital object identifier.</summary>
    internal string SourceDoi { get; set; }

    /// <summary>The source model's universally unique identifier.</summary>
    internal string SourceUuid { get; set; }

    /// <summary>The source model's repository.</summary>
    internal string SourceRepoUrl { get; set; }

    /// <summary>The tags the card carries, followed by its pipeline tag.</summary>
    internal List<string> Tags { get; set; }

    /// <summary>The languages the card names.</summary>
    internal List<string> Languages { get; set; }

    /// <summary>Applies the heuristics that fill the metadata, in the order the converter applies them.</summary>
    /// <param name="card">The model card, empty when there is none.</param>
    /// <param name="config">The checkpoint's configuration.</param>
    /// <param name="modelId">The model identifier the caller supplied.</param>
    /// <param name="totalParameters">The model's parameter count.</param>
    /// <returns>The metadata.</returns>
    internal static ModelMetadata Load(ModelCard card, HuggingFaceConfig config, string modelId,
        long totalParameters)
    {
        var metadata = new ModelMetadata();
        metadata.ApplyModelCard(card);
        metadata.ApplyNameOrPath(config, totalParameters);
        metadata.ApplyModelId(modelId, totalParameters);
        return metadata;
    }

    /// <summary>Writes the metadata, in the order the inference engine's converter writes it.</summary>
    /// <param name="writer">The file being built.</param>
    internal void WriteTo(GgufWriter writer)
    {
        writer.AddString("general.name", Name);
        writer.AddString("general.author", Author);
        writer.AddString("general.version", Version);
        writer.AddString("general.organization", Organization);
        writer.AddString("general.finetune", Finetune);
        writer.AddString("general.basename", Basename);
        writer.AddString("general.description", Description);
        writer.AddString("general.quantized_by", QuantizedBy);
        writer.AddString("general.size_label", SizeLabel);
        writer.AddString("general.license", License);
        writer.AddString("general.license.name", LicenseName);
        writer.AddString("general.license.link", LicenseLink);
        writer.AddString("general.source.url", SourceUrl);
        writer.AddString("general.source.doi", SourceDoi);
        writer.AddString("general.source.uuid", SourceUuid);
        writer.AddString("general.source.repo_url", SourceRepoUrl);
        if (Tags != null)
        {
            writer.AddArray("general.tags", GgufValueType.String, Tags.ToArray());
        }

        if (Languages != null)
        {
            writer.AddArray("general.languages", GgufValueType.String, Languages.ToArray());
        }
    }

    private void ApplyModelCard(ModelCard card)
    {
        UseString(card, "name", value => Name = Name ?? value);
        UseString(card, "author", value => Author = Author ?? value);
        UseString(card, "version", value => Version = Version ?? value);
        UseString(card, "organization", value => Organization = Organization ?? value);
        UseString(card, "description", value => Description = Description ?? value);
        UseString(card, "finetune", value => Finetune = Finetune ?? value);
        UseString(card, "basename", value => Basename = Basename ?? value);
        UseString(card, "size_label", value => SizeLabel = SizeLabel ?? value);
        UseString(card, "url", value => SourceUrl = SourceUrl ?? value);
        UseString(card, "doi", value => SourceDoi = SourceDoi ?? value);
        UseString(card, "uuid", value => SourceUuid = SourceUuid ?? value);
        UseString(card, "repo_url", value => SourceRepoUrl = SourceRepoUrl ?? value);

        UseString(card, "model_name", value => Name = Name ?? value);
        UseString(card, "model_author", value => Author = Author ?? value);
        UseString(card, "model_version", value => Version = Version ?? value);
        UseString(card, "model_organization", value => Organization = Organization ?? value);
        UseString(card, "model_description", value => Description = Description ?? value);
        UseString(card, "model_finetune", value => Finetune = Finetune ?? value);
        UseString(card, "model_basename", value => Basename = Basename ?? value);
        UseString(card, "model_size_label", value => SizeLabel = SizeLabel ?? value);
        UseString(card, "model_url", value => SourceUrl = SourceUrl ?? value);
        UseString(card, "model_doi", value => SourceDoi = SourceDoi ?? value);
        UseString(card, "model_uuid", value => SourceUuid = SourceUuid ?? value);
        UseString(card, "model_repo_url", value => SourceRepoUrl = SourceRepoUrl ?? value);

        UseString(card, "model_creator", value => Author = Author ?? value);
        UseString(card, "model_type", value => Basename = Basename ?? value);

        UseString(card, "license", value => License = License ?? value);
        UseString(card, "license_name", value => LicenseName = LicenseName ?? value);
        UseString(card, "license_link", value => LicenseLink = LicenseLink ?? value);

        Tags = AppendStrings(card, "tags", Tags);
        Tags = AppendStrings(card, "pipeline_tag", Tags);
        Languages = AppendStrings(card, "languages", Languages);
        Languages = AppendStrings(card, "language", Languages);
    }

    private void ApplyNameOrPath(HuggingFaceConfig config, long totalParameters)
    {
        string nameOrPath = config == null ? null : config.GetString("_name_or_path");
        if (nameOrPath == null || CountSlashes(nameOrPath) > 1)
        {
            return;
        }

        ApplyComponents(ModelIdComponents.Parse(nameOrPath, totalParameters));
    }

    private void ApplyModelId(string modelId, long totalParameters)
    {
        ApplyComponents(ModelIdComponents.Parse(modelId, totalParameters));
    }

    private void ApplyComponents(ModelIdComponents components)
    {
        if (components == null)
        {
            return;
        }

        if (Name == null && components.FullName != null)
        {
            Name = ModelIdComponents.IdToTitle(components.FullName);
        }

        if (Organization == null && components.Organization != null)
        {
            Organization = ModelIdComponents.IdToTitle(components.Organization);
        }

        if (Basename == null && components.Basename != null)
        {
            Basename = components.Basename;
        }

        if (Finetune == null && components.Finetune != null)
        {
            Finetune = components.Finetune;
        }

        if (Version == null && components.Version != null)
        {
            Version = components.Version;
        }

        if (SizeLabel == null && components.SizeLabel != null)
        {
            SizeLabel = components.SizeLabel;
        }
    }

    private static void UseString(ModelCard card, string key, Action<string> assign)
    {
        if (card.TryGetString(key, out string value))
        {
            assign(value);
        }
    }

    private static List<string> AppendStrings(ModelCard card, string key, List<string> target)
    {
        if (!card.TryGetStrings(key, out IReadOnlyList<string> values))
        {
            return target;
        }

        List<string> result = target ?? new List<string>();
        for (int i = 0; i < values.Count; i++)
        {
            result.Add(values[i]);
        }

        return result;
    }

    private static int CountSlashes(string value)
    {
        int count = 0;
        foreach (char character in value)
        {
            if (character == '/')
            {
                count++;
            }
        }

        return count;
    }
}
