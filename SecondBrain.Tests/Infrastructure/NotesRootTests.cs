using Microsoft.Extensions.Options;
using SecondBrain.Infrastructure;

namespace SecondBrain.Tests.Infrastructure;

public class NotesRootTests
{
    [Fact]
    public void ExplicitPath_IsUsed() =>
        Assert.Equal("/x/y", new NotesRoot(Options.Create(new StorageOptions { NotesRootPath = "/x/y" })).Path);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankPath_FallsBackToUserProfile(string configured)
    {
        var path = new NotesRoot(Options.Create(new StorageOptions { NotesRootPath = configured })).Path;
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SecondBrain", "notes");
        Assert.Equal(expected, path);
    }

    [Fact]
    public void DefaultFallback_IsInsideTestHome()
    {
        var path = new NotesRoot(Options.Create(new StorageOptions())).Path;
        Assert.StartsWith(Support.TestEnvironment.Home, path);
    }
}
