using SecondBrain.Infrastructure;

namespace SecondBrain.Tests.Infrastructure;

public class OptionsAndHeadersTests
{
    [Fact]
    public void Headers_SetBaseAddressAndTimeout()
    {
        using var client = new HttpClient();
        HttpClientHeaders.Apply(client, new HttpClientOptions { BaseAddress = "http://h/v1/", TimeoutSeconds = 7 });

        Assert.Equal(new Uri("http://h/v1/"), client.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(7), client.Timeout);
    }

    [Fact]
    public void Headers_ReplaceApiKeyPlaceholder()
    {
        using var client = new HttpClient();
        HttpClientHeaders.Apply(client, new HttpClientOptions
        {
            BaseAddress = "http://h/",
            ApiKey = "sekret",
            Headers = { ["Authorization"] = "Bearer {ApiKey}" }
        });

        Assert.Equal("Bearer sekret", client.DefaultRequestHeaders.GetValues("Authorization").Single());
    }

    [Fact]
    public void Headers_WithoutApiKey_KeepPlaceholderVerbatim()
    {
        using var client = new HttpClient();
        HttpClientHeaders.Apply(client, new HttpClientOptions
        {
            BaseAddress = "http://h/",
            Headers = { ["X-Test"] = "{ApiKey}" }
        });

        Assert.Equal("{ApiKey}", client.DefaultRequestHeaders.GetValues("X-Test").Single());
    }

    [Fact]
    public void Headers_OverwriteExistingHeader()
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("X-A", "old");
        HttpClientHeaders.Apply(client, new HttpClientOptions { BaseAddress = "http://h/", Headers = { ["X-A"] = "new" } });

        Assert.Equal("new", client.DefaultRequestHeaders.GetValues("X-A").Single());
    }

    [Fact]
    public void Headers_InvalidBaseAddress_Throws() =>
        Assert.ThrowsAny<Exception>(() => HttpClientHeaders.Apply(new HttpClient(), new HttpClientOptions { BaseAddress = "" }));

    [Fact]
    public void Defaults()
    {
        Assert.Equal(30, new HttpClientOptions().TimeoutSeconds);
        Assert.Equal(1536u, new VectorIndexOptions().VectorSize);
        Assert.Equal("", new StorageOptions().NotesRootPath);
        Assert.Empty(new PluginsOptions().Mcp);
        Assert.True(new McpServerOptions().Enabled);
        Assert.Equal(30, new McpServerOptions().TimeoutSeconds);
    }

    [Fact]
    public void CompressionPrompt_DemandsAllFourFields()
    {
        var prompt = new CompressionOptions().SystemPrompt;
        foreach (var field in new[] { "\"title\"", "\"content\"", "\"tags\"", "\"definitions\"" })
            Assert.Contains(field, prompt);
    }

    [Fact]
    public void AnswerPrompt_MentionsAnsweredField() =>
        Assert.Contains("\"answered\"", new AnswerSynthesisOptions().SystemPrompt);
}
