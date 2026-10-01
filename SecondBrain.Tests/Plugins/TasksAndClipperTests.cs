using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SecondBrain.Plugins.Tasks;
using SecondBrain.Plugins.WebClipper;
using SecondBrain.Tests.Support;

namespace SecondBrain.Tests.Plugins;

public class TaskScannerTests
{
    [Fact]
    public void Scan_ParsesOpenDoneAndDue()
    {
        var note = Sample.Note(raw: "Intro\n- [ ] zadzwonic 2026-10-05\n* [x] zrobione\n  - [X] zagniezdzone\n- [ ]brak spacji\n-[ ] zle");

        var tasks = TaskScanner.Scan("Praca", note).ToList();

        Assert.Equal(3, tasks.Count);
        Assert.Equal(new DateOnly(2026, 10, 5), tasks[0].Due);
        Assert.False(tasks[0].Done);
        Assert.True(tasks[1].Done && tasks[2].Done);
        Assert.Null(tasks[1].Due);
        Assert.All(tasks, t => Assert.Equal("Praca", t.Folder));
    }

    [Fact]
    public void Scan_InvalidDate_IsNoDue() =>
        Assert.Null(TaskScanner.Scan("f", Sample.Note(raw: "- [ ] cos 2026-13-45")).Single().Due);

    [Fact]
    public void OpenSorted_OverdueFirst_UndatedLast_DoneHidden()
    {
        var note = Sample.Note(raw: "- [ ] bez daty\n- [ ] pozno 2026-12-01\n- [x] gotowe 2026-01-01\n- [ ] wczesnie 2026-01-02");

        var sorted = TaskScanner.OpenSorted(TaskScanner.Scan("f", note));

        Assert.Equal(["wczesnie 2026-01-02", "pozno 2026-12-01", "bez daty"], sorted.Select(t => t.Text));
        Assert.True(TaskScanner.IsOverdue(sorted[0], new DateOnly(2026, 6, 1)));
        Assert.False(TaskScanner.IsOverdue(sorted[1], new DateOnly(2026, 6, 1)));
        Assert.False(TaskScanner.IsOverdue(sorted[2], new DateOnly(2026, 6, 1)));
    }
}

public class TasksToolAndTabTests : IDisposable
{
    private readonly Stack _s = new();

    public void Dispose() => _s.Dispose();

    private async Task SeedAsync()
    {
        _s.Compressor.Factory = _ => new("Plan", "c", ["tag"], []);
        await _s.Pipeline.CreateFolderAsync("Praca");
        await _s.Pipeline.AddAsync("Praca", "plan\n- [ ] raport 2026-01-01\n- [x] gotowe");
    }

    [Fact]
    public async Task ListTasks_ReturnsOpenOnly_AndIncludeDoneAddsRest()
    {
        await SeedAsync();
        var tool = new ListTasksTool(_s.Index, _s.Store);

        using var open = JsonDocument.Parse(await tool.ExecuteAsync(Sample.Args("{}")));
        using var all = JsonDocument.Parse(await tool.ExecuteAsync(Sample.Args(new { includeDone = true })));

        Assert.Single(open.RootElement.EnumerateArray());
        Assert.Equal("raport 2026-01-01", open.RootElement[0].GetProperty("Text").GetString());
        Assert.Equal(2, all.RootElement.GetArrayLength());
        Assert.False(tool.IsMutating);
    }

    [Fact]
    public async Task Tab_Load_CountsOpenAndOverdue_AndTitleReflectsIt()
    {
        await SeedAsync();
        var tab = new TasksTab(_s.Index, _s.Store, _s.Services);
        var titles = new List<string?>();
        tab.PropertyChanged += (_, e) => titles.Add(e.PropertyName);

        await tab.OnActivatedAsync(default);

        Assert.True(tab.HasTasks);
        Assert.Single(tab.Tasks);
        Assert.Equal("Zadania (1, 1 po terminie)", tab.Title);
        Assert.Contains(nameof(TasksTab.Title), titles);
    }

    [Fact]
    public async Task Tab_NoNotes_Empty()
    {
        var tab = new TasksTab(_s.Index, _s.Store, _s.Services);
        await tab.LoadAsync();
        Assert.False(tab.HasTasks);
        Assert.Equal("Zadania (0)", tab.Title);
    }
}

public class WebClipperTests
{
    private static (WebClipper Clipper, StubHttpHandler Http, StubHttpClientFactory Factory) Make(string html = "<html><title>Tytul &amp; co</title><body><nav>menu</nav><script>x()</script><p>Tresc</p></body></html>")
    {
        var http = new StubHttpHandler((req, _) => req.RequestUri!.Host == "example.com"
            ? (HttpStatusCode.OK, html)
            : (HttpStatusCode.OK, JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = "# Czysty\n\nTresc" } } } })));
        var factory = new StubHttpClientFactory(http);
        return (new WebClipper(factory, Options.Create(new WebClipperOptions { Model = "wm", SystemPrompt = "SYS", MaxChars = 50 })), http, factory);
    }

    [Theory]
    [InlineData("https://example.com/a", true)]
    [InlineData("  http://example.com  ", true)]
    [InlineData("ftp://example.com", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("to nie url", false)]
    [InlineData(null, false)]
    public void TryParseUrl_OnlyHttpAndHttps(string? text, bool expected) =>
        Assert.Equal(expected, WebClipper.TryParseUrl(text, out _));

    [Fact]
    public void HtmlToText_DropsNoise_KeepsTitleAndText()
    {
        var text = WebClipper.HtmlToText("<title>A &amp; B</title><nav>menu</nav><style>p{}</style><p>Jeden</p><p>Dwa&nbsp;x</p>");
        Assert.StartsWith("Tytul strony: A & B", text);
        Assert.Contains("Jeden", text);
        Assert.DoesNotContain("menu", text);
        Assert.DoesNotContain("p{}", text);
    }

    [Fact]
    public async Task Clip_SendsCleanedTextToLlm_AndAppendsSource()
    {
        var (clipper, http, factory) = Make();

        var result = await clipper.ClipAsync("https://example.com/a");

        Assert.Equal("# Czysty\n\nTresc\n\nŹródło: https://example.com/a", result);
        Assert.Equal(2, http.Requests.Count);
        using var body = http.LastBody;
        Assert.Equal("wm", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("SYS", body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.Contains("Tresc", body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString());
        Assert.Equal("WebClipper", factory.LastName);
    }

    [Fact]
    public async Task Clip_TruncatesLongPageToMaxChars()
    {
        var (clipper, http, _) = Make("<p>" + new string('x', 500) + "</p>");
        await clipper.ClipAsync("https://example.com/long");
        using var body = http.LastBody;
        Assert.Equal(50, body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!.Length);
    }

    [Fact]
    public async Task Clip_InvalidUrl_ThrowsWithoutAnyRequest()
    {
        var (clipper, http, _) = Make();
        await Assert.ThrowsAsync<ArgumentException>(() => clipper.ClipAsync("file:///etc/passwd"));
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task Clip_FetchError_Throws()
    {
        var http = StubHttpHandler.Json("nope", HttpStatusCode.NotFound);
        var clipper = new WebClipper(new StubHttpClientFactory(http), Options.Create(new WebClipperOptions()));
        await Assert.ThrowsAsync<HttpRequestException>(() => clipper.ClipAsync("https://example.com/x"));
    }

    [Fact]
    public async Task ClipUrlTool_SavesNote_AndDescribeAsksQuestion()
    {
        using var s = new Stack();
        s.Compressor.Factory = _ => new("Strona", "c", ["tag"], []);
        await s.Pipeline.CreateFolderAsync("Web");
        var (clipper, _, _) = Make();
        var tool = new ClipUrlTool(clipper, s.Pipeline);
        var args = Sample.Args(new { folder = "Web", url = "https://example.com/a" });

        var result = await tool.ExecuteAsync(args);

        Assert.Contains("Web", result);
        var note = Assert.Single(await s.Store.ListAsync("Web"));
        Assert.Contains("Źródło: https://example.com/a", note.RawContent);
        Assert.True(tool.IsMutating);
        Assert.Contains("?", tool.Describe(args));
    }
}
