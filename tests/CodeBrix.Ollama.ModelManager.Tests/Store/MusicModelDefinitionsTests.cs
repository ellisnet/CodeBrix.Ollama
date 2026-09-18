using System.Collections.Generic;
using System.Linq;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// The offline half of the music-model work. Every seeded definition is checked against itself and
/// against the library's own name grammar, pull options and file filter, so that a definition which
/// cannot possibly work is caught without a byte being downloaded. The gated tests in
/// <see cref="MusicModelLiveTests"/> then check the same definitions against their publishers.
/// </summary>
public sealed class MusicModelDefinitionsTests
{
    private const string SkyTntLogPath =
        "logs/version_0/events.out.tfevents.1727438892.autodl-container-14ba49b3e1-5960be9d.1410.0";

    private const long TwoGibibytes = 2147483648L;

    /// <summary>
    /// The name of every definition. The theories take a name rather than a definition because a name
    /// is a string the test runner can serialise, and so each row runs on its own.
    /// </summary>
    public static TheoryData<string> AllNames
    {
        get
        {
            var names = new TheoryData<string>();
            foreach (MusicModel model in MusicModelDefinitions.All)
            {
                names.Add(model.Definition.Name);
            }

            return names;
        }
    }

    [Fact]
    public void All_holds_thirteen_definitions() =>
        MusicModelDefinitions.All.Should().HaveCount(13);

    [Fact]
    public void All_names_every_definition_once() =>
        MusicModelDefinitions.All.Select(model => model.Definition.Name).Should().OnlyHaveUniqueItems();

    [Fact]
    public void The_gates_are_the_three_expected_environment_variables()
    {
        //Act and assert
        MusicModelDefinitions.LiveTestsGate.Should().Be("CODEBRIX_OLLAMA_RUN_LIVE_TESTS");
        MusicModelDefinitions.MuseCocoTestsGate.Should().Be("CODEBRIX_OLLAMA_RUN_MUSECOCO_TESTS");
        MusicModelDefinitions.LargeMusicTestsGate.Should().Be("CODEBRIX_OLLAMA_RUN_LARGE_MUSIC_TESTS");
    }

    /// <remarks>
    /// Three of the four are pulled by <see cref="MusicModelLiveTests"/> and cost about 1.05 GiB
    /// together; the fourth, the ONNX pair, is pulled by the export tests in the Python test project,
    /// which need the live gate and the Python gate at once.
    /// </remarks>
    [Fact]
    public void The_live_gate_opens_four_definitions_that_download_under_two_gibibytes()
    {
        //Arrange
        IReadOnlyList<MusicModel> gated = MusicModelDefinitions.All
            .Where(model => model.Gate == MusicModelDefinitions.LiveTestsGate)
            .ToList();

        //Act
        long total = gated.Sum(model => model.ExpectedBytes);

        //Assert
        gated.Should().HaveCount(4);
        total.Should().BeLessThan(TwoGibibytes);
    }

    [Theory]
    [MemberData(nameof(AllNames))]
    public void Definition_name_parses_in_the_stores_own_name_grammar(string name)
    {
        //Act
        bool parsed = ModelName.TryParse(name, out ModelName result);

        //Assert
        parsed.Should().BeTrue();
        result.IsFullyQualified.Should().BeTrue();
    }

    [Fact]
    public void ModelName_reads_the_parts_of_a_hugging_face_definition_name()
    {
        //Act
        ModelName name = ModelName.Parse(MusicModelDefinitions.SkyTntMidiModelTv2oMedium.Definition.Name);

        //Assert
        name.Host.Should().Be("hf.co");
        name.Namespace.Should().Be("skytnt");
        name.Model.Should().Be("midi-model-tv2o-medium");
        name.Tag.Should().Be(ModelName.DefaultTag);
    }

    [Fact]
    public void ModelName_reads_the_parts_of_a_storage_bucket_definition_name()
    {
        //Act
        ModelName name = ModelName.Parse(
            MusicModelDefinitions.MagentaMusicTransformerUnconditional.Definition.Name);

        //Assert
        name.Host.Should().Be("storage.googleapis.com");
        name.Namespace.Should().Be("magentadata");
        name.Model.Should().Be("music-transformer");
        name.Tag.Should().Be("unconditional-16");
    }

    [Theory]
    [MemberData(nameof(AllNames))]
    public void ToPullOptions_carries_the_source_the_repository_the_revision_the_filter_and_the_files(string name)
    {
        //Arrange
        BundleDefinition definition = Find(name).Definition;

        //Act
        PullOptions options = definition.ToPullOptions();

        //Assert
        options.Source.Should().Be(definition.Source);
        options.Repository.Should().Be(definition.Repository);
        options.Filter.Should().BeSameAs(definition.Filter);
        options.Files.Should().Equal(definition.Files);
        options.Revision.Should().Be(
            definition.Source == PullSource.HuggingFaceFiles ? definition.Revision : null);
    }

    [Theory]
    [MemberData(nameof(AllNames))]
    public void Every_expected_file_is_one_the_definitions_own_filter_keeps(string name)
    {
        //Arrange
        MusicModel model = Find(name);

        //Act
        IReadOnlyList<string> rejected = model.ExpectedFiles
            .Where(file => !model.Definition.Filter.ShouldInclude(file.Path))
            .Select(file => file.Path)
            .ToList();

        //Assert
        rejected.Should().BeEmpty();
    }

    [Fact]
    public void The_live_gate_skytnt_filter_leaves_the_onnx_pair_the_duplicate_weights_and_the_logs_behind()
    {
        //Arrange
        FileFilter filter = MusicModelDefinitions.SkyTntMidiModelTv2oMedium.Definition.Filter;

        //Act and assert
        filter.ShouldInclude("onnx/model_base.onnx").Should().BeFalse();
        filter.ShouldInclude("pytorch_model.bin").Should().BeFalse();
        filter.ShouldInclude(SkyTntLogPath).Should().BeFalse();
        filter.ShouldInclude("README.md").Should().BeTrue();
        filter.ShouldInclude("model.safetensors").Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(AllNames))]
    public void Every_expected_sha256_is_sixty_four_lower_case_hexadecimal_characters(string name)
    {
        //Arrange
        MusicModel model = Find(name);

        //Act
        IReadOnlyList<string> malformed = model.ExpectedFiles
            .Where(file => !IsLowerCaseHex(file.Sha256, 64))
            .Select(file => file.Path)
            .ToList();

        //Assert
        malformed.Should().BeEmpty();
        model.ExpectedFiles.Should().NotBeEmpty();
    }

    [Theory]
    [MemberData(nameof(AllNames))]
    public void ExpectedRevision_is_a_commit_for_a_hugging_face_definition_and_null_for_a_file_list(string name)
    {
        //Arrange
        MusicModel model = Find(name);

        //Act
        string revision = model.ExpectedRevision;

        //Assert
        if (model.Definition.Source == PullSource.HuggingFaceFiles)
        {
            revision.Should().Be(model.Definition.Revision);
            IsLowerCaseHex(revision, 40).Should().BeTrue();
        }
        else
        {
            revision.Should().BeNull();
        }
    }

    /// <summary>
    /// Finds the definition a theory row names.
    /// </summary>
    /// <param name="name">The name the definition is stored under.</param>
    /// <returns>The music model that name belongs to.</returns>
    private static MusicModel Find(string name) =>
        MusicModelDefinitions.All.Single(model => model.Definition.Name == name);

    /// <summary>
    /// Whether a string is exactly the expected number of lower-case hexadecimal characters.
    /// </summary>
    /// <param name="value">The string to test.</param>
    /// <param name="length">How many characters it must have.</param>
    /// <returns><see langword="true"/> when the string is hexadecimal and the right length.</returns>
    private static bool IsLowerCaseHex(string value, int length)
    {
        if (value == null || value.Length != length)
        {
            return false;
        }

        foreach (char character in value)
        {
            bool digit = character >= '0' && character <= '9';
            bool letter = character >= 'a' && character <= 'f';
            if (!digit && !letter)
            {
                return false;
            }
        }

        return true;
    }
}
