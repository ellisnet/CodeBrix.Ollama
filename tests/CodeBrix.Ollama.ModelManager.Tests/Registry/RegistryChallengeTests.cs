using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests; //was previously: ollama/ollama server/auth_test.go;

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
    public void Parse_with_ollama_test_cases_reads_every_value(string header, string realm, string service, string scope)
    {
        //Act
        RegistryChallenge challenge = RegistryChallenge.Parse(header);

        //Assert
        challenge.Realm.Should().Be(realm);
        challenge.Service.Should().Be(service);
        challenge.Scope.Should().Be(scope);
    }

    [Fact]
    public void Parse_with_null_header_reads_empty_values()
    {
        //Act
        RegistryChallenge challenge = RegistryChallenge.Parse(null);

        //Assert
        challenge.Realm.Should().Be("");
        challenge.Service.Should().Be("");
        challenge.Scope.Should().Be("");
    }

    [Fact]
    public void Parse_without_bearer_prefix_still_reads_values()
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
    public void Parse_with_comma_inside_quoted_value_keeps_the_comma()
    {
        //Act
        RegistryChallenge challenge = RegistryChallenge.Parse(
            "Bearer realm=\"https://auth.test/token\",service=\"one,two\",scope=\"repo:a:pull,repo:b:pull\"");

        //Assert
        challenge.Service.Should().Be("one,two");
        challenge.Scope.Should().Be("repo:a:pull,repo:b:pull");
    }

    [Fact]
    public void Parse_with_quote_not_followed_by_comma_keeps_the_quote()
    {
        //Act
        RegistryChallenge challenge = RegistryChallenge.Parse("Bearer realm=\"a\"b\",service=\"registry\"");

        //Assert
        challenge.Realm.Should().Be("a\"b");
        challenge.Service.Should().Be("registry");
    }

    [Fact]
    public void Parse_with_missing_key_reads_empty_value()
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
    public void Parse_with_truncated_header_reads_empty_value()
    {
        //Act
        RegistryChallenge challenge = RegistryChallenge.Parse("Bearer realm=");

        //Assert
        challenge.Realm.Should().Be("");
    }
}
