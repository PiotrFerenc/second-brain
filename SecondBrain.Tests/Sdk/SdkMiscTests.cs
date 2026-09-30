using System.Globalization;
using Avalonia.Media;
using SecondBrain.Core;
using SecondBrain.Plugins.Sdk;
using SecondBrain.Plugins.Sdk.Converters;

namespace SecondBrain.Tests.Sdk;

public class AutoLinkConverterTests
{
    private static string Convert(object? v) => (string)AutoLinkConverter.Instance.Convert(v, typeof(string), null, CultureInfo.InvariantCulture)!;

    [Fact]
    public void WrapsBareUrl() =>
        Assert.Equal("zobacz [https://a.pl/x](https://a.pl/x) teraz", Convert("zobacz https://a.pl/x teraz"));

    [Theory]
    [InlineData("http://a.pl.", "[http://a.pl](http://a.pl).")]
    [InlineData("(https://a.pl)", "([https://a.pl](https://a.pl))")]
    [InlineData("https://a.pl, dalej", "[https://a.pl](https://a.pl), dalej")]
    [InlineData("https://a.pl!?", "[https://a.pl](https://a.pl)!?")]
    [InlineData("\"https://a.pl\"", "\"[https://a.pl](https://a.pl)\"")]
    public void TrailingPunctuation_StaysOutsideLink(string input, string expected) =>
        Assert.Equal(expected, Convert(input));

    [Fact]
    public void MultipleUrls_AllWrapped() =>
        Assert.Equal("[http://a](http://a) i [https://b](https://b)", Convert("http://a i https://b"));

    [Fact]
    public void PlainText_Unchanged() =>
        Assert.Equal("nic tu nie ma", Convert("nic tu nie ma"));

    [Fact]
    public void NonString_ReturnedAsIs()
    {
        var o = new object();
        Assert.Same(o, AutoLinkConverter.Instance.Convert(o, typeof(string), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Null_ReturnedAsIs() =>
        Assert.Null(AutoLinkConverter.Instance.Convert(null, typeof(string), null, CultureInfo.InvariantCulture));

    [Fact]
    public void Empty_ReturnedAsIs() =>
        Assert.Equal("", Convert(""));

    [Fact]
    public void ConvertBack_NotSupported() =>
        Assert.Throws<NotSupportedException>(() => AutoLinkConverter.Instance.ConvertBack("x", typeof(string), null, CultureInfo.InvariantCulture));
}

public class PinLabelConverterTests
{
    [Theory]
    [InlineData(true, "Odepnij")]
    [InlineData(false, "Przypnij")]
    [InlineData(null, "Przypnij")]
    [InlineData("x", "Przypnij")]
    public void Label(object? pinned, string expected) =>
        Assert.Equal(expected, PinLabelConverter.Instance.Convert(pinned, typeof(string), null, CultureInfo.InvariantCulture));

    [Fact]
    public void ConvertBack_NotSupported() =>
        Assert.Throws<NotSupportedException>(() => PinLabelConverter.Instance.ConvertBack("x", typeof(bool), null, CultureInfo.InvariantCulture));
}

public class FolderAccentConverterTests
{
    private static IBrush Convert(object? v) => (IBrush)FolderAccentConverter.Instance.Convert(v, typeof(IBrush), null, CultureInfo.InvariantCulture);

    [Fact]
    public void SameName_SameColor() =>
        Assert.Equal(((SolidColorBrush)Convert("Praca")).Color, ((SolidColorBrush)Convert("Praca")).Color);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(42)]
    public void NonName_FallsBackToFirstAccent(object? v) =>
        Assert.Equal(Color.Parse("#cba6f7"), ((SolidColorBrush)Convert(v)).Color);

    [Fact]
    public void ManyNames_StayWithinPalette()
    {
        var colors = Enumerable.Range(0, 200).Select(i => ((SolidColorBrush)Convert($"folder{i}")).Color).Distinct().ToList();
        Assert.InRange(colors.Count, 2, 10);
    }

    [Fact]
    public void ConvertBack_NotSupported() =>
        Assert.Throws<NotSupportedException>(() => FolderAccentConverter.Instance.ConvertBack("x", typeof(string), null, CultureInfo.InvariantCulture));
}

public class NoteItemTests
{
    [Fact]
    public void CopiesAllFields()
    {
        var id = Guid.NewGuid();
        var parent = Guid.NewGuid();
        var created = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        var item = new NoteItem(id, "Tytul", ["a", "b"], 0.5f, "raw", "/p.md", parent, true, "Folder", created);

        Assert.Equal(id, item.Id);
        Assert.Equal("Tytul", item.Title);
        Assert.Equal(["a", "b"], item.TagList);
        Assert.Equal("a, b", item.Tags);
        Assert.Equal(0.5f, item.Score);
        Assert.Equal("raw", item.RawContent);
        Assert.Equal("/p.md", item.FilePath);
        Assert.Equal(parent, item.ParentId);
        Assert.True(item.Pinned);
        Assert.Equal("Folder", item.Folder);
        Assert.Equal(created, item.CreatedAt);
    }

    [Fact]
    public void NoTags_EmptyString() =>
        Assert.Equal("", new NoteItem(Guid.NewGuid(), "t", [], 0, "", "", null, false, "f").Tags);

    [Fact]
    public void CreatedAt_DefaultsToDefault() =>
        Assert.Equal(default, new NoteItem(Guid.NewGuid(), "t", [], 0, "", "", null, false, "f").CreatedAt);
}

public class NullShellTests
{
    [Fact]
    public void Shell_HasEmptyState()
    {
        var shell = new NullShell();
        Assert.Null(shell.SelectedFolder);
        Assert.Null(shell.SelectedNote);
        Assert.Null(shell.SelectedSearchResult);
        Assert.Empty(shell.Folders);
        Assert.Null(shell.TreeFilter);
    }

    [Fact]
    public async Task Shell_Navigation_IsNoOp()
    {
        var shell = new NullShell();
        shell.ShowTab("x");
        shell.Search("q");
        shell.ShowNote(new NoteItem(Guid.NewGuid(), "t", [], 0, "", "", null, false, "f"));
        await shell.RefreshTreeAsync();
    }

    [Fact]
    public void Shell_TreeFilter_IsSettable()
    {
        var shell = new NullShell { TreeFilter = n => true };
        Assert.NotNull(shell.TreeFilter);
    }

    [Fact]
    public void Shell_EventSubscription_DoesNotThrow()
    {
        var shell = new NullShell();
        static void H() { }
        shell.SelectedNoteChanged += H;
        shell.SelectedNoteChanged -= H;
    }

    [Fact]
    public void Shell_TopLevel_Throws() =>
        Assert.Throws<InvalidOperationException>(() => new NullShell().TopLevel);

    [Fact]
    public async Task Editor_HoldsPlainState()
    {
        var editor = new NullEditorContext { Text = "abc", Status = "ok", IsBusy = true };
        editor.SetFolder("f");

        Assert.Equal("abc", editor.Text);
        Assert.Equal("ok", editor.Status);
        Assert.True(editor.IsBusy);
        Assert.Null(editor.Folder);
        await editor.SaveAsync();
        await editor.OpenQuickNoteAsync("f");
    }
}

public class PluginRuntimeTests
{
    [Fact]
    public void Services_DefaultToNull_AndAreSettable()
    {
        var before = PluginRuntime.Services;
        try
        {
            PluginRuntime.Services = null;
            Assert.Null(PluginRuntime.Services);
        }
        finally
        {
            PluginRuntime.Services = before;
        }
    }
}
