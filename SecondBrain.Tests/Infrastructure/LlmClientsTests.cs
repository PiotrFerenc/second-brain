using System.Net;
using Microsoft.Extensions.Options;
using SecondBrain.Core;
using SecondBrain.Infrastructure;
using SecondBrain.Tests.Support;

namespace SecondBrain.Tests.Infrastructure;

public class FabrykaCompressorTests
{
    private static FabrykaCompressor Make(StubHttpHandler h, StubHttpClientFactory? f = null) =>
        new(f ?? new StubHttpClientFactory(h), Options.Create(new CompressionOptions { Model = "m1", SystemPrompt = "SYS" }));

    [Fact]
    public async Task ParsesLlmJson()
    {
        var h = StubHttpHandler.Chat("""{"title":"T","content":"C","tags":["a"],"definitions":[{"term":"X","definition":"Y"}]}""");
        var r = await Make(h).CompressAsync("tekst");

        Assert.Equal("T", r.Title);
        Assert.Equal("C", r.CompressedContent);
        Assert.Equal(["a"], r.Tags);
        Assert.Equal("X", Assert.Single(r.Definitions).Term);
    }

    [Fact]
    public async Task ParsesJson_WithSurroundingWhitespace_AndCaseInsensitiveNames()
    {
        var h = StubHttpHandler.Chat("\n  {\"Title\":\"T\",\"Content\":\"C\",\"Tags\":[],\"Definitions\":[]}  \n");
        var r = await Make(h).CompressAsync("x");
        Assert.Equal("T", r.Title);
    }

    [Fact]
    public async Task SendsModelSystemPromptAndUserText_UsingCompressionClient()
    {
        var h = StubHttpHandler.Chat("""{"title":"T","content":"C","tags":[],"definitions":[]}""");
        var factory = new StubHttpClientFactory(h);
        await Make(h, factory).CompressAsync("moja notatka");

        Assert.Equal("Compression", factory.LastName);
        using var body = h.LastBody;
        Assert.Equal("m1", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("json_object", body.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        var messages = body.RootElement.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("SYS", messages[0].GetProperty("content").GetString());
        Assert.Equal("moja notatka", messages[1].GetProperty("content").GetString());
        Assert.EndsWith("chat/completions", h.Requests[0].Request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task HttpError_Throws() =>
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Make(StubHttpHandler.Json("{}", HttpStatusCode.Unauthorized)).CompressAsync("x"));

    [Fact]
    public async Task NonJsonContent_Throws() =>
        await Assert.ThrowsAnyAsync<Exception>(() => Make(StubHttpHandler.Chat("nie json")).CompressAsync("x"));

    [Fact]
    public async Task JsonNull_ThrowsInvalidOperation() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => Make(StubHttpHandler.Chat("null")).CompressAsync("x"));
}

public class FabrykaAnswerSynthesizerTests
{
    private static FabrykaAnswerSynthesizer Make(StubHttpHandler h, StubHttpClientFactory? f = null) =>
        new(f ?? new StubHttpClientFactory(h), Options.Create(new AnswerSynthesisOptions { Model = "m2", SystemPrompt = "SYS" }));

    [Fact]
    public async Task ParsesAnswer()
    {
        var r = await Make(StubHttpHandler.Chat("""{"answered":true,"answer":"42"}""")).SynthesizeAsync("q", [Sample.Note()]);
        Assert.True(r.Answered);
        Assert.Equal("42", r.Answer);
    }

    [Fact]
    public async Task ParsesNotAnswered()
    {
        var r = await Make(StubHttpHandler.Chat("""{"answered":false,"answer":"brak"}""")).SynthesizeAsync("q", []);
        Assert.False(r.Answered);
    }

    [Fact]
    public async Task PromptContainsNumberedNotesAndQuestion()
    {
        var h = StubHttpHandler.Chat("""{"answered":true,"answer":"x"}""");
        var factory = new StubHttpClientFactory(h);
        await Make(h, factory).SynthesizeAsync("Ile?", [Sample.Note(title: "Pierwsza", raw: "R1"), Sample.Note(title: "Druga", raw: "R2")]);

        Assert.Equal("AnswerSynthesis", factory.LastName);
        using var body = h.LastBody;
        var user = body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
        Assert.Contains("Notatka 1: Pierwsza\nR1", user);
        Assert.Contains("Notatka 2: Druga\nR2", user);
        Assert.EndsWith("Pytanie: Ile?", user);
    }

    [Fact]
    public async Task HttpError_Throws() =>
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Make(StubHttpHandler.Json("{}", HttpStatusCode.BadGateway)).SynthesizeAsync("q", []));
}

public class FabrykaEmbedderTests
{
    [Fact]
    public async Task ReturnsFirstEmbedding_AndSendsModelAndInput()
    {
        var h = StubHttpHandler.Json("""{"data":[{"embedding":[0.5,1.5]}]}""");
        var factory = new StubHttpClientFactory(h);
        var e = new FabrykaEmbedder(factory, Options.Create(new EmbeddingOptions { Model = "emb" }));

        var v = await e.EmbedAsync("tekst");

        Assert.Equal([0.5f, 1.5f], v);
        Assert.Equal("Embedding", factory.LastName);
        using var body = h.LastBody;
        Assert.Equal("emb", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("tekst", body.RootElement.GetProperty("input").GetString());
    }

    [Fact]
    public async Task HttpError_Throws()
    {
        var e = new FabrykaEmbedder(new StubHttpClientFactory(StubHttpHandler.Json("{}", HttpStatusCode.Forbidden)), Options.Create(new EmbeddingOptions()));
        await Assert.ThrowsAsync<HttpRequestException>(() => e.EmbedAsync("x"));
    }
}
