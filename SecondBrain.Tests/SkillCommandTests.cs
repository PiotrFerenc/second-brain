using SecondBrain.Core;

namespace SecondBrain.Tests;

public class SkillCommandTests
{
    private static readonly string[] Names = ["porzadkowanie-tagow", "odnosnik-at"];

    [Fact]
    public void KnownSkill_ExpandsWithTask()
    {
        var result = SkillCommand.Expand("/Porzadkowanie-Tagow folder Praca", Names);
        Assert.Contains("\"porzadkowanie-tagow\"", result);
        Assert.EndsWith("Zadanie: folder Praca", result);
    }

    [Fact]
    public void KnownSkill_WithoutTask_HasNoTaskPart() =>
        Assert.DoesNotContain("Zadanie", SkillCommand.Expand("/odnosnik-at", Names));

    [Theory]
    [InlineData("/nieznany cos")]
    [InlineData("zwykla wiadomosc /odnosnik-at")]
    [InlineData("/")]
    public void OtherText_Unchanged(string message) =>
        Assert.Equal(message, SkillCommand.Expand(message, Names));
}
