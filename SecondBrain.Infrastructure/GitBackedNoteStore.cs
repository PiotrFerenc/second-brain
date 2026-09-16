using CliWrap;
using CliWrap.Buffered;
using Microsoft.Extensions.Options;
using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

// Dekorator INoteStore: po kazdej mutacji na dysku robi cichy "git commit" w katalogu
// notatek, wiec uzytkownik dostaje darmowa historie wersji/backup bez zadnej akcji.
// Owija FileNoteStore zamiast go modyfikowac, zeby nie mieszac sie w logike zapisu.
public class GitBackedNoteStore(INoteStore inner, IOptions<StorageOptions> options) : INoteStore
{
    // ponytail: to samo wyliczenie sciezki co w FileNoteStore._root - musi sie zgadzac,
    // ale nie warto wydzielac wspolnej metody dla dwoch miejsc uzycia w ramach tego zadania.
    private readonly string _root = string.IsNullOrWhiteSpace(options.Value.NotesRootPath)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SecondBrain", "notes")
        : options.Value.NotesRootPath;

    // Commity leca w tle (nie blokuja zwrotu z SaveAsync/etc. do UI), ale jeden po drugim -
    // rownolegle "git commit" na tym samym repo walczylyby o .git/index.lock. Lock tylko
    // na doklejenie kolejnego ogniwa lancucha, nie na sam commit (ten dalej biegnie w tle).
    private readonly object _commitChainLock = new();
    private Task _commitChain = Task.CompletedTask;

    public Task<string> SaveAsync(string folder, Note note, CancellationToken ct = default) =>
        WithCommitAsync(() => inner.SaveAsync(folder, note, ct), $"Zapisano notatke: {note.Title}");

    public Task<Note> LoadAsync(string filePath, CancellationToken ct = default) =>
        inner.LoadAsync(filePath, ct);

    public Task<IReadOnlyList<Note>> ListAsync(string folder, CancellationToken ct = default) =>
        inner.ListAsync(folder, ct);

    public Task DeleteFolderAsync(string folder, CancellationToken ct = default) =>
        WithCommitAsync(() => inner.DeleteFolderAsync(folder, ct), $"Usunieto folder: {folder}");

    public Task<string> MoveToTrashAsync(string folder, string filePath, CancellationToken ct = default) =>
        WithCommitAsync(() => inner.MoveToTrashAsync(folder, filePath, ct), $"Notatka w koszu (folder {folder})");

    public Task<IReadOnlyList<TrashedNote>> ListTrashAsync(CancellationToken ct = default) =>
        inner.ListTrashAsync(ct);

    public Task<TrashedNote> RestoreFromTrashAsync(string trashPath, CancellationToken ct = default) =>
        WithCommitAsync(() => inner.RestoreFromTrashAsync(trashPath, ct), "Przywrocono notatke z kosza");

    public Task PurgeTrashAsync(string trashPath, CancellationToken ct = default) =>
        WithCommitAsync(() => inner.PurgeTrashAsync(trashPath, ct), "Trwale usunieto notatke z kosza");

    public Task<IReadOnlyList<NoteTemplate>> ListTemplatesAsync(CancellationToken ct = default) =>
        inner.ListTemplatesAsync(ct);

    public Task LogGapAsync(string query, CancellationToken ct = default) =>
        WithCommitAsync(() => inner.LogGapAsync(query, ct), $"Zapisano luke w wiedzy: {query}");

    public Task<IReadOnlyList<KnowledgeGap>> ListGapsAsync(CancellationToken ct = default) =>
        inner.ListGapsAsync(ct);

    public Task ResolveGapAsync(string path, CancellationToken ct = default) =>
        WithCommitAsync(() => inner.ResolveGapAsync(path, ct), "Odrzucono luke w wiedzy");

    public Task SaveGlossaryEntryAsync(string term, string definition, string sourceTitle, CancellationToken ct = default) =>
        WithCommitAsync(() => inner.SaveGlossaryEntryAsync(term, definition, sourceTitle, ct), $"Slownik: {term}");

    public Task<IReadOnlyList<GlossaryEntry>> ListGlossaryAsync(CancellationToken ct = default) =>
        inner.ListGlossaryAsync(ct);

    public Task<IReadOnlyList<FolderedNote>> MergeTagsAsync(string[] fromTags, string toTag, CancellationToken ct = default) =>
        WithCommitAsync(() => inner.MergeTagsAsync(fromTags, toTag, ct), $"Scalono tagi: {string.Join(", ", fromTags)} -> {toTag}");

    public Task RecordFactVersionAsync(string subject, string statement, string sourceTitle, CancellationToken ct = default) =>
        WithCommitAsync(() => inner.RecordFactVersionAsync(subject, statement, sourceTitle, ct), $"Wersja faktu: {subject}");

    public Task<IReadOnlyList<FactVersion>> ListFactHistoryAsync(string subject, CancellationToken ct = default) =>
        inner.ListFactHistoryAsync(subject, ct);

    private async Task WithCommitAsync(Func<Task> action, string message)
    {
        await action();
        ScheduleCommit(message);
    }

    private async Task<T> WithCommitAsync<T>(Func<Task<T>> action, string message)
    {
        var result = await action();
        ScheduleCommit(message);
        return result;
    }

    private void ScheduleCommit(string message)
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
