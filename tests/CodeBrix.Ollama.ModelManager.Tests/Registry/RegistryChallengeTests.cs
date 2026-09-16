// Ported from Ollama (https://github.com/ollama/ollama), MIT License, Copyright (c) Ollama. Source: server/auth_test.go at commit a43fad18.
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers <see cref="RegistryChallenge.Parse"/>, including the table from Ollama's own
/// TestParseRegistryChallenge and the quote handling its getValue helper is particular about.
/// </summary>
public sealed class RegistryChallengeTests
{
    [Theory]
    [InlineData(
        "Bearer realm=\"https://auth.example.com/token\",service=\"registry\",scope=\"repo:foo:pull\"",
        "https://auth.example.com/token", "registry", "repo:foo:pull")]
    [InlineData(
        "Bearer realm=\"https://r.ollama.ai/v2/token\",service=\"ollama\",scope=\"-\"",
        "https://r.ollama.ai/v2/token", "ollama", "-")]
    [InlineData("", "", "", "")]
    public void Parse_WithOllamaTestCases_ReadsEveryValue(string header, string realm, string service, string scope)
    {
        //Act
        RegistryChallenge challenge = RegistryChallenge.Parse(header);

        //Assert
        challenge.Realm.Should().Be(realm);
        challenge.Service.Should().Be(service);
        challenge.Scope.Should().Be(scope);
    }

    [Fact]
    public void Parse_WithNullHeader_ReadsEmptyValues()
    {
        //Act
        RegistryChallenge challenge = RegistryChallenge.Parse(null);

        //Assert
        challenge.Realm.Should().Be("");
        challenge.Service.Should().Be("");
        challenge.Scope.Should().Be("");
    }

    [Fact]
    public void Parse_WithoutBearerPrefix_StillReadsValues()
    {
        //Act
        RegistryChallenge challenge = RegistryChallenge.Parse(
            "realm=\"https://auth.test/token\",service=\"registry\",scope=\"repo:library/x:pull\"");

        //Assert
        challenge.Realm.Should().Be("https://auth.test/token");
        challenge.Service.Should().Be("registry");
        challenge.Scope.Should().Be("repo:library/x:pull");
    }

    [Fact]
    public void Parse_WithCommaInsideQuotedValue_KeepsTheComma()
    {
        //Act
        RegistryChallenge challenge = RegistryChallenge.Parse(
            "Bearer realm=\"https://auth.test/token\",service=\"one,two\",scope=\"repo:a:pull,repo:b:pull\"");

        //Assert
        challenge.Service.Should().Be("one,two");
        challenge.Scope.Should().Be("repo:a:pull,repo:b:pull");
    }

    [Fact]
    public void Parse_WithQuoteNotFollowedByComma_KeepsTheQuote()
    {
        //Act
        RegistryChallenge challenge = RegistryChallenge.Parse("Bearer realm=\"a\"b\",service=\"registry\"");

        //Assert
        challenge.Realm.Should().Be("a\"b");
        challenge.Service.Should().Be("registry");
    }

    [Fact]
    public void Parse_WithMissingKey_ReadsEmptyValue()
    {
        //Act
        RegistryChallenge challenge = RegistryChallenge.Parse(
            "Bearer realm=\"https://auth.test/token\",service=\"registry\"");

        //Assert
        challenge.Realm.Should().Be("https://auth.test/token");
        challenge.Service.Should().Be("registry");
        challenge.Scope.Should().Be("");
    }

    [Fact]
    public void Parse_WithTruncatedHeader_ReadsEmptyValue()
    {
        //Act
        RegistryChallenge challenge = RegistryChallenge.Parse("Bearer realm=");

        //Assert
        challenge.Realm.Should().Be("");
    }
}
