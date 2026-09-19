using CodeBrix.Ollama.ModelManager;
using ModelQueryTool.ModelAccess.Models;
using SilverAssertions;
using Xunit;

namespace ModelQueryTool.ModelAccess.Tests;

/// <summary>
/// The one model the application obtains, written down once. These are the numbers the publisher served
/// when they were last read; a run of the gated staging test is what says whether they still hold.
/// </summary>
public class KnownModelsTests
{
    /// <summary>The store name parses as a fully qualified model name on the publisher's own host.</summary>
    [Fact]
    public void Qwen35_is_named_in_the_grammar_the_store_parses()
    {
        //Act
        bool parsed = ModelName.TryParse(KnownModels.Qwen35.Name, out ModelName name);

        //Assert
        parsed.Should().BeTrue();
        name.IsFullyQualified.Should().BeTrue();
        name.Host.Should().Be("hf.co");
        name.Namespace.Should().Be("unsloth");
        name.Model.Should().Be("Qwen3.5-35B-A3B-GGUF");
        name.Tag.Should().Be("Q4_K_M");
    }

    /// <summary>The expected total is the weights, the projector and the configuration together.</summary>
    [Fact]
    public void Qwen35_expects_the_weights_the_projector_and_the_configuration_together()
    {
        //Arrange
        const long weights = 22_016_023_168L;
        const long projector = 899_283_648L;
        const long configuration = 488L;

        //Assert
        KnownModels.Qwen35.ExpectedTotalBytes.Should().Be(weights + projector + configuration);
    }

    /// <summary>The expected weights digest is sixty-four lower-case hexadecimal characters.</summary>
    [Fact]
    public void Qwen35_expects_the_published_weights_digest() =>
        KnownModels.Qwen35.WeightsSha256.Should()
            .Be("3b46d1066bc91cc2d613e3bc22ce691dd77e6f0d33c9060690d24ce6de494375");

    /// <summary>The display name says both what the model is and how far it is quantized.</summary>
    [Fact]
    public void Qwen35_has_a_display_name_that_names_the_quantization() =>
        KnownModels.Qwen35.DisplayName.Should().Be("Qwen 3.5 35B-A3B (Q4_K_M)");
}
