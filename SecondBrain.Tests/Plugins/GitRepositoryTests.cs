using System.Diagnostics;
using SecondBrain.Core;
using SecondBrain.Plugins.History;
using SecondBrain.Tests.Support;

namespace SecondBrain.Tests.Plugins;

public class GitRepositoryTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly GitRepository _git;

    public GitRepositoryTests() => _git = new GitRepository(_root.Notes);

    public void Dispose() => _root.Dispose();

    private void Git(params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = _root.Path, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"git {string.Join(' ', args)}: {p.StandardError.ReadToEnd()}");
    }

    private void Commit(string file, string content, string message)
    {
        var path = _root.Combine(file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        Git("add", "-A");
        Git("commit", "--no-gpg-sign", "-q", "-m", message);
    }

    private static async Task<T> PollAsync<T>(Func<Task<T>> get, Func<T, bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        T value;
        do
        {
            value = await get();
            if (done(value))
                break;
            await Task.Delay(50);
        } while (DateTime.UtcNow < deadline);
        return value;
    }

    [Fact]
    public async Task ListCommits_NotARepo_ReturnsEmpty() =>
        Assert.Empty(await _git.ListCommitsAsync());

    [Fact]
    public async Task ListCommits_NewestFirst_WithMessageAndHashes()
    {
        Git("init", "-q");
        Commit("a.md", "1", "pierwszy");
        Commit("a.md", "2", "drugi | z rurka");

        var commits = await _git.ListCommitsAsync();

        Assert.Equal(["drugi | z rurka", "pierwszy"], commits.Select(c => c.Message));
        Assert.Equal(40, commits[0].Hash.Length);
        Assert.StartsWith(commits[0].ShortHash, commits[0].Hash);
        Assert.InRange(commits[0].When, DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow.AddMinutes(2));
    }

    [Fact]
    public async Task ListCommits_RespectsLimit()
    {
        Git("init", "-q");
        for (var i = 0; i < 4; i++)
            Commit("a.md", i.ToString(), $"c{i}");

        Assert.Equal(2, (await _git.ListCommitsAsync(limit: 2)).Count);
    }

    [Fact]
    public async Task Diff_FirstCommit_AllLinesAdded()
    {
        Git("init", "-q");
        Commit("notes/a.md", "jeden\ndwa\n", "init");
        var hash = (await _git.ListCommitsAsync())[0].Hash;

        var file = Assert.Single(await _git.GetCommitDiffAsync(hash));

        Assert.Equal("notes/a.md", file.Path);
        Assert.Equal(["jeden", "dwa"], file.Lines.Where(l => l.IsAdded).Select(l => l.Text));
        Assert.DoesNotContain(file.Lines, l => l.IsRemoved);
    }

    [Fact]
    public async Task Diff_Modification_HasHunkAddedRemovedAndContext()
    {
        Git("init", "-q");
        Commit("a.md", "jeden\ndwa\ntrzy\n", "init");
        Commit("a.md", "jeden\nDWA\ntrzy\ncztery\n", "zmiana");
        var hash = (await _git.ListCommitsAsync())[0].Hash;

        var file = Assert.Single(await _git.GetCommitDiffAsync(hash));

        Assert.Contains(file.Lines, l => l.IsHunk && l.Text.StartsWith("@@"));
        Assert.Equal(["dwa"], file.Lines.Where(l => l.IsRemoved).Select(l => l.Text));
        Assert.Equal(["DWA", "cztery"], file.Lines.Where(l => l.IsAdded).Select(l => l.Text));
        Assert.Contains(file.Lines, l => l.Kind == DiffLineKind.Context && l.Text == "jeden");
    }

    [Fact]
    public async Task Diff_MultipleFiles_SeparatedByPath()
    {
        Git("init", "-q");
        Directory.CreateDirectory(_root.Combine("d"));
        File.WriteAllText(_root.Combine("a.md"), "a\n");
        File.WriteAllText(_root.Combine("d", "b.md"), "b\n");
        Git("add", "-A");
        Git("commit", "--no-gpg-sign", "-q", "-m", "dwa pliki");
        var hash = (await _git.ListCommitsAsync())[0].Hash;

        var files = await _git.GetCommitDiffAsync(hash);

        Assert.Equal(["a.md", "d/b.md"], files.Select(f => f.Path).Order());
    }

    [Fact]
    public async Task Diff_DeletedFile_ShowsRemovedLines()
    {
        Git("init", "-q");
        Commit("a.md", "x\ny\n", "init");
        File.Delete(_root.Combine("a.md"));
        Git("add", "-A");
        Git("commit", "--no-gpg-sign", "-q", "-m", "usuniecie");
        var hash = (await _git.ListCommitsAsync())[0].Hash;

        var file = Assert.Single(await _git.GetCommitDiffAsync(hash));

        Assert.Equal(["x", "y"], file.Lines.Where(l => l.IsRemoved).Select(l => l.Text));
    }

    [Fact]
    public async Task Diff_UnknownHash_ReturnsEmpty() =>
        Assert.Empty(await _git.GetCommitDiffAsync("0123456789abcdef0123456789abcdef01234567"));

    [Fact]
    public async Task ScheduleCommit_InitsRepoAndCommitsEverything()
    {
        File.WriteAllText(_root.Combine("n.md"), "tresc");

        _git.ScheduleCommit("Zapisano notatke: n");

        var commits = await PollAsync(() => _git.ListCommitsAsync(), c => c.Count >= 1);
        Assert.Equal("Zapisano notatke: n", Assert.Single(commits).Message);
        Assert.True(Directory.Exists(_root.Combine(".git")));
    }

    [Fact]
    public async Task ScheduleCommit_Sequential_KeepsOrder_AndSkipsEmptyCommits()
    {
        File.WriteAllText(_root.Combine("a.md"), "1");
        _git.ScheduleCommit("pierwszy");
        await PollAsync(() => _git.ListCommitsAsync(), c => c.Count >= 1);

        File.WriteAllText(_root.Combine("a.md"), "2");
        _git.ScheduleCommit("drugi");
        _git.ScheduleCommit("bez zmian");   // nic do zacommitowania - git odrzuci, backup to lyknie
        await PollAsync(() => _git.ListCommitsAsync(), c => c.Count >= 2);
        await Task.Delay(300);

        var commits = await _git.ListCommitsAsync();
        Assert.Equal(["drugi", "pierwszy"], commits.Select(c => c.Message));
    }

    [Fact]
    public async Task GitCommitOnChange_Handler_SchedulesCommit()
    {
        File.WriteAllText(_root.Combine("h.md"), "x");

        await new GitCommitOnChange(_git).HandleAsync(new StorageChanged("z handlera"));

        var commits = await PollAsync(() => _git.ListCommitsAsync(), c => c.Count >= 1);
        Assert.Equal("z handlera", Assert.Single(commits).Message);
    }

    [Fact]
    public async Task HistoryTab_LoadsCommits_OnActivation_AndDiffOnSelection()
    {
        Git("init", "-q");
        Commit("a.md", "x\n", "init");
        var tab = new HistoryTab(_git);

        await tab.OnActivatedAsync(CancellationToken.None);

        Assert.True(tab.HasCommits);
        tab.SelectedCommit = tab.Commits[0];
        await PollAsync(() => Task.FromResult(tab.HasCommitDiff), d => d);
        Assert.True(tab.HasCommitDiff);
        Assert.Equal("a.md", Assert.Single(tab.CommitDiffFiles).Path);

        tab.SelectedCommit = null;
        await PollAsync(() => Task.FromResult(tab.HasCommitDiff), d => !d);
        Assert.Empty(tab.CommitDiffFiles);
    }

    [Fact]
    public async Task HistoryTab_EmptyRepo_NoCommits()
    {
        var tab = new HistoryTab(_git);
        await tab.OnActivatedAsync(CancellationToken.None);
        Assert.False(tab.HasCommits);
    }
}
