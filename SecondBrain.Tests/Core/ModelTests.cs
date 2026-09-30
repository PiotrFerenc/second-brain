using System.Text.Json;
using SecondBrain.Core;
using SecondBrain.Tests.Support;

namespace SecondBrain.Tests.Core;

public class ModelTests
{
    [Fact]
    public void Note_Defaults()
    {
        var n = new Note(Guid.NewGuid(), "t", "r", "c", [], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        Assert.Equal("", n.FilePath);
        Assert.Null(n.ParentId);
        Assert.False(n.Pinned);
    }

    [Fact]
    public void Note_WithExpression_KeepsOtherFields()
    {
        var n = Sample.Note(title: "a", pinned: false);
        var pinned = n with { Pinned = true };
        Assert.True(pinned.Pinned);
        Assert.Equal(n.Id, pinned.Id);
        Assert.Equal("a", pinned.Title);
    }

    [Fact]
    public void CompressionResult_DeserializesFromLlmJson()
    {
        const string json = """{"title":"T","content":"C","tags":["x","y"],"definitions":[{"term":"RAG","definition":"Retrieval"}]}""";
        var r = JsonSerializer.Deserialize<CompressionResult>(json)!;

        Assert.Equal("T", r.Title);
        Assert.Equal("C", r.CompressedContent);
        Assert.Equal(["x", "y"], r.Tags);
        Assert.Equal(new GlossaryTerm("RAG", "Retrieval"), Assert.Single(r.Definitions));
    }

    [Fact]
    public void AnswerResult_DeserializesFromLlmJson()
    {
        var r = JsonSerializer.Deserialize<AnswerResult>("""{"answered":true,"answer":"tak"}""")!;
        Assert.True(r.Answered);
        Assert.Equal("tak", r.Answer);
    }

    [Theory]
    [InlineData(DiffLineKind.Added, "+", true, false, false)]
    [InlineData(DiffLineKind.Removed, "-", false, true, false)]
    [InlineData(DiffLineKind.Hunk, "", false, false, true)]
    [InlineData(DiffLineKind.Context, "", false, false, false)]
    public void DiffLine_FlagsAndMarker(DiffLineKind kind, string marker, bool added, bool removed, bool hunk)
    {
        var line = new DiffLine(kind, "x");
        Assert.Equal(marker, line.Marker);
        Assert.Equal(added, line.IsAdded);
        Assert.Equal(removed, line.IsRemoved);
        Assert.Equal(hunk, line.IsHunk);
    }
}
