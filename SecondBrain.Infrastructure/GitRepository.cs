using CliWrap;
using CliWrap.Buffered;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

// Katalog notatek jako repo gita: darmowa historia wersji/backup bez akcji uzytkownika.
// Commity leca w tle po kazdym StorageChanged (GitCommitOnChange), zakladka "Historia"
// czyta log i diff.
public sealed class GitRepository(NotesRoot notesRoot)
{
    private readonly string _root = notesRoot.Path;

    // Commity leca w tle (nie blokuja zwrotu z zapisu do UI), ale jeden po drugim -
    // rownolegle "git commit" na tym samym repo walczylyby o .git/index.lock. Lock tylko
    // na doklejenie kolejnego ogniwa lancucha, nie na sam commit (ten dalej biegnie w tle).
    private readonly object _commitChainLock = new();
    private Task _commitChain = Task.CompletedTask;

    // Zakladka "Historia" (jak SourceTree/GitKraken): cale repo notatek, bez filtru po pliku.
    public async Task<IReadOnlyList<CommitEntry>> ListCommitsAsync(int limit = 200, CancellationToken ct = default)
    {
        var result = await Cli.Wrap("git")
            .WithArguments(["log", $"-n{limit}", "--format=%H|||%h|||%aI|||%s"])
            .WithWorkingDirectory(_root)
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(ct);

        if (result.ExitCode != 0)
            return [];

        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split("|||", 4))
            .Where(parts => parts.Length == 4)
            .Select(parts => new CommitEntry(parts[0], parts[1], DateTimeOffset.Parse(parts[2]), parts[3]))
            .ToList();
    }

    // "git show" na pierwszym commicie (bez rodzica) dziala tak samo jak na kazdym innym -
    // pokazuje cala tresc jako same dodane linie, wiec nie trzeba specjalnego przypadku.
    public async Task<IReadOnlyList<DiffFile>> GetCommitDiffAsync(string hash, CancellationToken ct = default)
    {
        var result = await Cli.Wrap("git")
            .WithArguments(["show", "--format=", "--no-color", hash])
            .WithWorkingDirectory(_root)
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(ct);

        if (result.ExitCode != 0)
            return [];

        return ParseDiff(result.StandardOutput);
    }

    private static IReadOnlyList<DiffFile> ParseDiff(string diffOutput)
    {
        var files = new List<DiffFile>();
        string? currentPath = null;
        List<DiffLine>? currentLines = null;

        void FlushCurrent()
        {
            if (currentPath is not null && currentLines is not null)
                files.Add(new DiffFile(currentPath, currentLines));
        }

        foreach (var line in diffOutput.Split('\n'))
        {
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                FlushCurrent();
                // "diff --git a/<path> b/<path>" - bierzemy sciezke "b/" (po zmianie), dziala
                // tez dla nowych/usunietych plikow bo git zawsze podaje oba warianty tutaj.
                var bIndex = line.LastIndexOf(" b/", StringComparison.Ordinal);
                currentPath = bIndex >= 0 ? line[(bIndex + 3)..] : line["diff --git ".Length..];
                currentLines = [];
            }
            else if (currentLines is null)
            {
                continue; // preambula przed pierwszym "diff --git" (nie powinno wystapic dla --format=)
            }
            else if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                currentLines.Add(new DiffLine(DiffLineKind.Hunk, line));
            }
            else if (line.StartsWith("+++", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal) ||
                     line.StartsWith("index ", StringComparison.Ordinal) || line.StartsWith("new file mode", StringComparison.Ordinal) ||
                     line.StartsWith("deleted file mode", StringComparison.Ordinal))
            {
                // metadane naglowka diffa - pomijamy, sciezka juz mamy z "diff --git"
            }
            else if (line.StartsWith('+'))
            {
                currentLines.Add(new DiffLine(DiffLineKind.Added, line[1..]));
            }
            else if (line.StartsWith('-'))
            {
                currentLines.Add(new DiffLine(DiffLineKind.Removed, line[1..]));
            }
            else if (line.StartsWith(' '))
            {
                currentLines.Add(new DiffLine(DiffLineKind.Context, line[1..]));
            }
        }

        FlushCurrent();
        return files;
    }

    public void ScheduleCommit(string message)
    {
        lock (_commitChainLock)
            _commitChain = _commitChain.ContinueWith(_ => CommitAsync(message), TaskScheduler.Default).Unwrap();
    }

    private async Task CommitAsync(string message)
    {
        try
        {
            Directory.CreateDirectory(_root);
            if (!Directory.Exists(Path.Combine(_root, ".git")))
                await RunGitAsync("init");

            await RunGitAsync("add", "-A");
            await RunGitAsync("commit", "--no-gpg-sign", "-m", message);
        }
        catch (Exception ex)
        {
            // Backup do gita jest najlepszego wysilku - awaria (brak gita, brak
            // user.name/user.email, brak zmian do zacommitowania) nie moze zepsuc
            // prawdziwej operacji na notatce.
            Console.Error.WriteLine($"Git backup notatek nie powiodl sie: {ex.Message}");
        }
    }

    private async Task RunGitAsync(params string[] args)
    {
        // Best-effort backup - musi nigdy nie zawiesic prawdziwej operacji na notatce
        // czekajac na interaktywny prompt (haslo GPG, login credential managera windows).
        // CliWrap domyslnie nie podpina stdin procesu-rodzica pod dziecko, wiec kazdy taki
        // prompt dostaje natychmiastowe EOF zamiast wisiec.
        var result = await Cli.Wrap("git")
            .WithArguments(args)
            .WithWorkingDirectory(_root)
            .WithValidation(CommandResultValidation.None)
            .WithEnvironmentVariables(env => env.Set("GIT_TERMINAL_PROMPT", "0"))
            .ExecuteBufferedAsync();

        if (result.ExitCode != 0)
            Console.Error.WriteLine($"Git backup notatek: 'git {string.Join(' ', args)}' zwrocilo {result.ExitCode}: {result.StandardError.Trim()}");
    }
}
