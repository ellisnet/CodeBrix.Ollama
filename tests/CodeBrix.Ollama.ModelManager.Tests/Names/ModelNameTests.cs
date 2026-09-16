// Ported from Ollama (https://github.com/ollama/ollama), MIT License, Copyright (c) Ollama. Source: types/model/name_test.go at commit a43fad18.
using System;
using System.IO;
using CodeBrix.Ollama.ModelManager;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Conformance tests for <see cref="ModelName"/>. The case tables are ported from Ollama's
/// types/model/name_test.go at commit a43fad18.
/// </summary>
public sealed class ModelNameTests
{
    private const string Part80 = "88888888888888888888888888888888888888888888888888888888888888888888888888888888";

    private const string Part350 = "33333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333333";

    // ---------------------------------------------------------------------
    // ParseBare parts, and the relative path of the same name after Parse.
    // Ported from TestParseNameParts.
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(
        "registry.ollama.ai/library/dolphin-mistral:7b-v2.6-dpo-laser-q6_K",
        "registry.ollama.ai", "library", "dolphin-mistral", "7b-v2.6-dpo-laser-q6_K", null,
        "registry.ollama.ai", "library", "dolphin-mistral", "7b-v2.6-dpo-laser-q6_K")]
    [InlineData(
        "scheme://host:port/namespace/model:tag",
        "host:port", "namespace", "model", "tag", "scheme",
        "host:port", "namespace", "model", "tag")]
    [InlineData(
        "host/namespace/model:tag",
        "host", "namespace", "model", "tag", null,
        "host", "namespace", "model", "tag")]
    [InlineData(
        "host:port/namespace/model:tag",
        "host:port", "namespace", "model", "tag", null,
        "host:port", "namespace", "model", "tag")]
    [InlineData(
        "host/namespace/model",
        "host", "namespace", "model", null, null,
        "host", "namespace", "model", "latest")]
    [InlineData(
        "host:port/namespace/model",
        "host:port", "namespace", "model", null, null,
        "host:port", "namespace", "model", "latest")]
    [InlineData(
        "namespace/model",
        null, "namespace", "model", null, null,
        "registry.ollama.ai", "namespace", "model", "latest")]
    [InlineData(
        "model",
        null, null, "model", null, null,
        "registry.ollama.ai", "library", "model", "latest")]
    [InlineData(
        "h/nn/mm:t",
        "h", "nn", "mm", "t", null,
        "h", "nn", "mm", "t")]
    [InlineData(
        Part80 + "/" + Part80 + "/" + Part80 + ":" + Part80,
        Part80, Part80, Part80, Part80, null,
        Part80, Part80, Part80, Part80)]
    [InlineData(
        Part350 + "/" + Part80 + "/" + Part80 + ":" + Part80,
        Part350, Part80, Part80, Part80, null,
        Part350, Part80, Part80, Part80)]
    public void ParseBare_SplitsIntoParts_ForEveryUpstreamCase(
        string input,
        string expectedHost,
        string expectedNamespace,
        string expectedModel,
        string expectedTag,
        string expectedScheme,
        string pathHost,
        string pathNamespace,
        string pathModel,
        string pathTag)
    {
        //Arrange
        string expectedPath = Path.Combine(pathHost, pathNamespace, pathModel, pathTag);

        //Act
        ModelName bare = ModelName.ParseBare(input);
        ModelName parsed = ModelName.Parse(input);

        //Assert
        bare.Host.Should().Be(expectedHost);
        bare.Namespace.Should().Be(expectedNamespace);
        bare.Model.Should().Be(expectedModel);
        bare.Tag.Should().Be(expectedTag);
        bare.ProtocolScheme.Should().Be(expectedScheme);
        parsed.ToRelativePath().Should().Be(expectedPath);
    }

    // ---------------------------------------------------------------------
    // IsValid, and the String() round trip for valid names.
    // Ported from TestNameIsValid (the shared testCases table).
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("", false)]
    [InlineData("_why/_the/_lucky:_stiff", true)]
    // minimal
    [InlineData("h/n/m:t", true)]
    [InlineData("host/namespace/model:tag", true)]
    [InlineData("host/namespace/model", false)]
    [InlineData("namespace/model", false)]
    [InlineData("model", false)]
    // long, but valid
    [InlineData(Part80 + "/" + Part80 + "/" + Part80 + ":" + Part80, true)]
    [InlineData(Part350 + "/" + Part80 + "/" + Part80 + ":" + Part80, true)]
    // bare minimum part sizes
    [InlineData("h/nn/mm:t", true)]
    // unqualified
    [InlineData("m", false)]
    [InlineData("n/m:", false)]
    [InlineData("h/n/m", false)]
    [InlineData("@t", false)]
    [InlineData("m@d", false)]
    // invalid
    [InlineData("^", false)]
    [InlineData("mm:", false)]
    [InlineData("/nn/mm", false)]
    [InlineData("//", false)]
    [InlineData("//mm", false)]
    [InlineData("hh//", false)]
    [InlineData("//mm:@", false)]
    [InlineData("00@", false)]
    [InlineData("@", false)]
    // not starting with an alphanumeric character
    [InlineData("-hh/nn/mm:tt", false)]
    [InlineData("hh/-nn/mm:tt", false)]
    [InlineData("hh/nn/-mm:tt", false)]
    [InlineData("hh/nn/mm:-tt", false)]
    // hosts
    [InlineData("host:https/namespace/model:tag", true)]
    // colon in a non-host part before the tag
    [InlineData("host/name:space/model:tag", false)]
    public void IsValid_MatchesUpstream_AndValidNamesRoundTrip(string input, bool expected)
    {
        //Act
        ModelName name = ModelName.ParseBare(input);

        //Assert
        name.IsValid.Should().Be(expected);
        name.IsFullyQualified.Should().Be(expected);
        if (expected)
        {
            name.ToString().Should().Be(input);
        }
    }

    [Fact]
    public void Parse_WithModelOnly_FillsDefaults()
    {
        //Act
        ModelName name = ModelName.Parse("xx");

        //Assert
        name.ToString().Should().Be("registry.ollama.ai/library/xx:latest");
        name.ProtocolScheme.Should().Be("https");
        name.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ParseBare_WithNull_ReturnsEmptyName()
    {
        //Act
        ModelName name = ModelName.ParseBare(null);

        //Assert
        name.Should().Be(default(ModelName));
        name.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("n/m:")]
    [InlineData("mm:")]
    [InlineData("//")]
    public void ParseBare_WithPromisedButEmptyPart_UsesMissingPart(string input)
    {
        //Act
        ModelName name = ModelName.ParseBare(input);

        //Assert
        string all = string.Concat(name.Host, name.Namespace, name.Model, name.Tag);
        all.Should().Contain(ModelName.MissingPart);
        name.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ParseBare_WithDigestSuffix_LeavesDigestAttached()
    {
        //Act
        ModelName name = ModelName.ParseBare("h/n/m:t@sha256-1000");

        //Assert
        name.Tag.Should().Be("t@sha256-1000");
        name.IsValid.Should().BeFalse();
    }

    // ---------------------------------------------------------------------
    // Part length and character rules. Ported from TestNameIsValidPart.
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("", false)]
    [InlineData("a", true)]
    [InlineData("a.", true)]
    [InlineData("a.b", true)]
    [InlineData("a:123", true)]
    [InlineData("a:123/aa/bb", false)]
    [InlineData("-a", false)]
    [InlineData(Part350, true)]
    [InlineData(Part350 + "3", false)]
    public void IsValid_ChecksHostPartRules(string host, bool expected)
    {
        //Act
        var name = new ModelName(host, "nn", "mm", "tt", null);

        //Assert
        name.IsValid.Should().Be(expected);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("mm", true)]
    [InlineData("m.m", true)]
    [InlineData("m_m", true)]
    [InlineData("m-m", true)]
    [InlineData("-h", false)]
    [InlineData("m:m", false)]
    [InlineData(Part80, true)]
    [InlineData(Part80 + "8", false)]
    public void IsValid_ChecksModelPartRules(string model, bool expected)
    {
        //Act
        var name = new ModelName("hh", "nn", model, "tt", null);

        //Assert
        name.IsValid.Should().Be(expected);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("tt", true)]
    [InlineData("t.t", true)]
    [InlineData("t:t", false)]
    [InlineData("-t", false)]
    [InlineData(Part80, true)]
    [InlineData(Part80 + "8", false)]
    public void IsValid_ChecksTagPartRules(string tag, bool expected)
    {
        //Act
        var name = new ModelName("hh", "nn", "mm", tag, null);

        //Assert
        name.IsValid.Should().Be(expected);
    }

    // ---------------------------------------------------------------------
    // IsValidNamespace. Ported from TestIsValidNamespace and the namespace
    // rows of TestNameIsValidPart.
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("", false)]
    [InlineData("a", true)]
    [InlineData("bb", true)]
    [InlineData("a.", false)]
    [InlineData("a:b", false)]
    [InlineData("a/b", false)]
    [InlineData("a:b/c", false)]
    [InlineData("a/b:c", false)]
    [InlineData("a/b:c/d", false)]
    [InlineData("a/b:c/d@e", false)]
    [InlineData("a/b:c/d@sha256-100", false)]
    [InlineData("himynameisjoe", true)]
    [InlineData("himynameisreallyreallyreallyreallylongbutitshouldstillbevalid", true)]
    [InlineData(Part80, true)]
    [InlineData(Part80 + "8", false)]
    public void IsValidNamespace_MatchesUpstream(string value, bool expected)
    {
        ModelName.IsValidNamespace(value).Should().Be(expected);
    }

    [Fact]
    public void IsValidNamespace_WithNull_ReturnsFalse()
    {
        ModelName.IsValidNamespace(null).Should().BeFalse();
    }

    // ---------------------------------------------------------------------
    // ToRelativePath and ParseFromRelativePath.
    // Ported from Name.Filepath and TestParseNameFromFilepath.
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("host/namespace/model/tag", "host", "namespace", "model", "tag")]
    [InlineData("host:port/namespace/model/tag", "host:port", "namespace", "model", "tag")]
    public void ParseFromRelativePath_WithFourValidParts_ReturnsName(
        string relativePath,
        string expectedHost,
        string expectedNamespace,
        string expectedModel,
        string expectedTag)
    {
        //Act
        ModelName name = ModelName.ParseFromRelativePath(relativePath);

        //Assert
        name.Host.Should().Be(expectedHost);
        name.Namespace.Should().Be(expectedNamespace);
        name.Model.Should().Be(expectedModel);
        name.Tag.Should().Be(expectedTag);
        name.ProtocolScheme.Should().BeNull();
        name.IsFullyQualified.Should().BeTrue();
    }

    [Theory]
    [InlineData("namespace/model/tag")]
    [InlineData("model/tag")]
    [InlineData("model")]
    [InlineData("../../model/tag")]
    [InlineData("namespace/tag")]
    [InlineData(".")]
    [InlineData("/path/to/random/file")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseFromRelativePath_WithUnusablePath_ReturnsDefault(string relativePath)
    {
        ModelName.ParseFromRelativePath(relativePath).Should().Be(default(ModelName));
    }

    [Fact]
    public void ToRelativePath_RoundTripsThroughParseFromRelativePath()
    {
        //Arrange
        ModelName name = ModelName.Parse("model");

        //Act
        string path = name.ToRelativePath();
        ModelName roundTripped = ModelName.ParseFromRelativePath(path);

        //Assert
        path.Should().Be(Path.Combine("registry.ollama.ai", "library", "model", "latest"));
        roundTripped.Should().Be(name);
    }

    [Fact]
    public void ToRelativePath_WhenNotFullyQualified_Throws()
    {
        //Arrange
        ModelName name = ModelName.ParseBare("model");

        //Act
        Action act = () => name.ToRelativePath();

        //Assert
        act.Should().Throw<InvalidOperationException>();
    }

    // ---------------------------------------------------------------------
    // DisplayShortest. Ported from TestDisplayShortest.
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("registry.ollama.ai/library/model:latest", "model:latest")]
    [InlineData("registry.ollama.ai/library/model:tag", "model:tag")]
    [InlineData("registry.ollama.ai/namespace/model:tag", "namespace/model:tag")]
    [InlineData("host/namespace/model:tag", "host/namespace/model:tag")]
    [InlineData("host/library/model:tag", "host/library/model:tag")]
    [InlineData("REGISTRY.OLLAMA.AI/LIBRARY/model:tag", "model:tag")]
    public void DisplayShortest_MatchesUpstream(string input, string expected)
    {
        ModelName.ParseBare(input).DisplayShortest().Should().Be(expected);
    }

    [Theory]
    [InlineData("host/namespace/model:tag", "namespace/model")]
    [InlineData("model", "library/model")]
    public void DisplayNamespaceModel_JoinsNamespaceAndModel(string input, string expected)
    {
        ModelName.Parse(input).DisplayNamespaceModel().Should().Be(expected);
    }

    // ---------------------------------------------------------------------
    // Merge.
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("model", "registry.ollama.ai", "library", "model", "latest", "https")]
    [InlineData("namespace/model", "registry.ollama.ai", "namespace", "model", "latest", "https")]
    [InlineData("host/namespace/model", "host", "namespace", "model", "latest", "https")]
    [InlineData("h/n/m:t", "h", "n", "m", "t", "https")]
    [InlineData("scheme://h/n/m:t", "h", "n", "m", "t", "scheme")]
    public void Merge_PrefersThePresentPartsOfTheFirstName(
        string input,
        string expectedHost,
        string expectedNamespace,
        string expectedModel,
        string expectedTag,
        string expectedScheme)
    {
        //Act
        ModelName merged = ModelName.Merge(ModelName.ParseBare(input), ModelName.Default);

        //Assert
        merged.Host.Should().Be(expectedHost);
        merged.Namespace.Should().Be(expectedNamespace);
        merged.Model.Should().Be(expectedModel);
        merged.Tag.Should().Be(expectedTag);
        merged.ProtocolScheme.Should().Be(expectedScheme);
    }

    [Fact]
    public void Merge_TakesTheModelFromTheFirstNameOnly()
    {
        //Arrange
        var first = new ModelName(null, null, null, null, null);
        var second = new ModelName("h", "n", "other", "t", "http");

        //Act
        ModelName merged = ModelName.Merge(first, second);

        //Assert
        merged.Model.Should().BeNull();
        merged.Host.Should().Be("h");
        merged.Namespace.Should().Be("n");
        merged.Tag.Should().Be("t");
        merged.ProtocolScheme.Should().Be("http");
    }

    // ---------------------------------------------------------------------
    // EqualFold and the ordinal equality members.
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("HOST/NAMESPACE/MODEL:TAG", "host/namespace/model:tag", true)]
    [InlineData("host/namespace/model:tag", "host/namespace/model:tag", true)]
    [InlineData("host/namespace/model:tag", "host/namespace/model:other", false)]
    [InlineData("host/namespace/model:tag", "other/namespace/model:tag", false)]
    public void EqualsIgnoreCase_ComparesPartsWithoutCase(string first, string second, bool expected)
    {
        ModelName.ParseBare(first).EqualsIgnoreCase(ModelName.ParseBare(second)).Should().Be(expected);
    }

    [Fact]
    public void Equals_IsOrdinalAndIgnoresTheProtocolScheme()
    {
        //Arrange
        var lower = new ModelName("h", "n", "m", "t", "https");
        var upper = new ModelName("H", "N", "M", "T", "https");
        var otherScheme = new ModelName("h", "n", "m", "t", "http");

        //Assert
        lower.Equals(upper).Should().BeFalse();
        lower.EqualsIgnoreCase(upper).Should().BeTrue();
        lower.Equals(otherScheme).Should().BeTrue();
        lower.GetHashCode().Should().Be(otherScheme.GetHashCode());
    }

    [Fact]
    public void EqualityOperators_MatchEquals()
    {
        //Arrange
        ModelName first = ModelName.Parse("model");
        ModelName second = ModelName.Parse("registry.ollama.ai/library/model:latest");
        ModelName third = ModelName.Parse("other");

        //Assert
        (first == second).Should().BeTrue();
        (first != second).Should().BeFalse();
        (first == third).Should().BeFalse();
        (first != third).Should().BeTrue();
        first.Equals((object)second).Should().BeTrue();
        first.Equals((object)"model").Should().BeFalse();
    }

    // ---------------------------------------------------------------------
    // BaseUrl.
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("model", "https", "registry.ollama.ai")]
    [InlineData("http://host/namespace/model:tag", "http", "host")]
    [InlineData("https://host:8080/namespace/model:tag", "https", "host")]
    public void BaseUrl_BuildsTheRegistryAddress(string input, string expectedScheme, string expectedHost)
    {
        //Act
        Uri baseUrl = ModelName.Parse(input).BaseUrl();

        //Assert
        baseUrl.Scheme.Should().Be(expectedScheme);
        baseUrl.Host.Should().Be(expectedHost);
    }

    [Fact]
    public void BaseUrl_WithPort_KeepsThePort()
    {
        ModelName.Parse("https://host:8080/namespace/model:tag").BaseUrl().Port.Should().Be(8080);
    }

    // ---------------------------------------------------------------------
    // Members that have no upstream counterpart.
    // ---------------------------------------------------------------------

    [Fact]
    public void Default_HoldsTheDefaultPartsAndNoModel()
    {
        //Act
        ModelName name = ModelName.Default;

        //Assert
        name.Host.Should().Be(ModelName.DefaultHost);
        name.Namespace.Should().Be(ModelName.DefaultNamespace);
        name.Tag.Should().Be(ModelName.DefaultTag);
        name.ProtocolScheme.Should().Be(ModelName.DefaultProtocolScheme);
        name.Model.Should().BeNull();
        name.IsValid.Should().BeFalse();
    }

    [Fact]
    public void CreateDefaults_BuildsTheDefaultsForTheParseOverload()
    {
        //Arrange
        ModelName defaults = ModelName.CreateDefaults("hf.co", "team", "v1", "http");

        //Act
        ModelName name = ModelName.Parse("model", defaults);

        //Assert
        defaults.Model.Should().BeNull();
        name.ToString().Should().Be("hf.co/team/model:v1");
        name.ProtocolScheme.Should().Be("http");
    }

    [Fact]
    public void Parse_WithDefaults_KeepsThePartsTheNameSupplies()
    {
        //Arrange
        ModelName defaults = ModelName.CreateDefaults("hf.co", "team", "v1", "http");

        //Act
        ModelName name = ModelName.Parse("other.host/other/model:tag", defaults);

        //Assert
        name.ToString().Should().Be("other.host/other/model:tag");
        name.ProtocolScheme.Should().Be("http");
    }

    [Theory]
    [InlineData("model", true)]
    [InlineData("h/n/m:t", true)]
    [InlineData("", false)]
    [InlineData("^", false)]
    [InlineData("mm:", false)]
    public void TryParse_ReportsWhetherTheParsedNameIsValid(string input, bool expected)
    {
        //Act
        bool parsed = ModelName.TryParse(input, out ModelName name);

        //Assert
        parsed.Should().Be(expected);
        name.IsValid.Should().Be(expected);
    }

    [Fact]
    public void Constructor_TreatsEmptyPartsAsAbsent()
    {
        //Act
        var name = new ModelName(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

        //Assert
        name.Host.Should().BeNull();
        name.Namespace.Should().BeNull();
        name.Model.Should().BeNull();
        name.Tag.Should().BeNull();
        name.ProtocolScheme.Should().BeNull();
        name.Should().Be(default(ModelName));
    }
}
