using SecondBrain.Core;
using SecondBrain.Tests.Support;

namespace SecondBrain.Tests.Core;

public class ToolArgsTests
{
    [Fact]
    public void Req_ReturnsString() =>
        Assert.Equal("x", Sample.Args("""{"a":"x"}""").Req("a"));

    [Fact]
    public void Req_MissingProperty_Throws() =>
        Assert.Throws<KeyNotFoundException>(() => Sample.Args("""{"a":"x"}""").Req("b"));

    [Fact]
    public void Opt_Missing_ReturnsNull() =>
        Assert.Null(Sample.Args("""{"a":"x"}""").Opt("b"));

    [Fact]
    public void Opt_ExplicitNull_ReturnsNull() =>
        Assert.Null(Sample.Args("""{"a":null}""").Opt("a"));

    [Fact]
    public void Opt_Present_ReturnsValue() =>
        Assert.Equal("x", Sample.Args("""{"a":"x"}""").Opt("a"));

    [Theory]
    [InlineData("""{"f":true}""", true)]
    [InlineData("""{"f":false}""", false)]
    public void Bool_ReadsValue(string json, bool expected) =>
        Assert.Equal(expected, Sample.Args(json).Bool("f"));

    [Fact]
    public void Bool_Missing_Throws() =>
        Assert.Throws<KeyNotFoundException>(() => Sample.Args("{}").Bool("f"));

    [Fact]
    public void ReqArr_ReadsStrings() =>
        Assert.Equal(["a", "b"], Sample.Args("""{"x":["a","b"]}""").ReqArr("x"));

    [Fact]
    public void ReqArr_NullElement_BecomesEmptyString() =>
        Assert.Equal(["a", ""], Sample.Args("""{"x":["a",null]}""").ReqArr("x"));

    [Fact]
    public void ReqArr_Missing_Throws() =>
        Assert.Throws<KeyNotFoundException>(() => Sample.Args("{}").ReqArr("x"));

    [Fact]
    public void OptArr_Missing_ReturnsEmpty() =>
        Assert.Empty(Sample.Args("{}").OptArr("x"));

    [Fact]
    public void OptArr_NotAnArray_ReturnsEmpty() =>
        Assert.Empty(Sample.Args("""{"x":"str"}""").OptArr("x"));

    [Fact]
    public void OptArr_Array_ReadsStrings() =>
        Assert.Equal(["1", "2"], Sample.Args("""{"x":["1","2"]}""").OptArr("x"));
}
