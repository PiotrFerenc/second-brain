using SecondBrain.Core;

namespace SecondBrain.Infrastructure;

// Notices: komunikaty dla uzytkownika dopisane przez handlery zdarzen (sprzecznosc, domkniete
// luki) - wywolujacy (edytor, agent, CLI) pokazuje je po swojemu.
public record AddNoteResult(Note Note, CompressionResult Result, IReadOnlyList<Note> Related, IReadOnlyList<string> Notices);

public record ImportResult(int Count, IReadOnlyList<string> Notices);

// Jedyne miejsce cyklu zycia notatki: kompresja -> zapis -> embedding -> upsert, a reszta
// (slownik, sprzecznosc + wersje faktow, domykanie luk, backup gita) to handlery zdarzen
// z szyny - patrz NoteEventHandlers. Wczesniej ta sekwencja byla skopiowana w edytorze,
// imporcie, edycji, agencie (x3) i CLI - i zdazyla sie rozjechac.
public sealed class NotePipeline(
    ICompressor compressor,
    IEmbedder embedder,
    IVectorIndex vectorIndex,
    INoteStore noteStore,
    IEventBus events)
{
    // chooseTags: wywolujacy moze ustalic tagi na podstawie wyniku kompresji i sasiadow
    // (edytor: tagi reczne albo auto-tagowanie z sasiadow); null = tagi z kompresji.
    // Handlery NoteCompressed moga je jeszcze zmienic.
    public async Task<AddNoteResult> AddAsync(
        string folder,
        string rawText,
        Guid? parentId = null,
        Func<CompressionResult, IReadOnlyList<Note>, string[]>? chooseTags = null,
        bool fromImport = false,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var result = await compressor.CompressAsync(rawText, ct);

        // "Auto-linkowanie" wlasnym wektorem notatki (bez LLM, bez ryzyka halucynacji tytulow).
        // Szukamy PRZED upsertem, wiec wynik nie zawiera jeszcze samej notatki.
        var vector = await embedder.EmbedAsync(result.CompressedContent, ct);
        var related = (await vectorIndex.SearchAsync(folder, vector, limit: 4, ct)).Select(r => r.Note).Take(3).ToList();

        var tags = (chooseTags?.Invoke(result, related) ?? result.Tags).ToList();
        await events.PublishAsync(new NoteCompressed(folder, rawText, result, tags, related), ct);

        var note = new Note(Guid.NewGuid(), result.Title, rawText, result.CompressedContent, [.. tags], now, now, ParentId: parentId);
        var path = await noteStore.SaveAsync(folder, note, ct);
        note = note with { FilePath = path };
        await vectorIndex.UpsertAsync(folder, note, vector, ct);

        var notices = new List<string>();
        await events.PublishAsync(new NoteAdded(folder, note, result, related, fromImport, notices), ct);
        await events.PublishAsync(new NotesChanged(), ct);

        return new AddNoteResult(note, result, related, notices);
    }

    // Edycja = rekompresja + nadpisanie tego samego pliku i wpisu w indeksie (Id i CreatedAt
    // z `existing` wyznaczaja sciezke, wiec nie powstaje duplikat).
    public async Task<Note> EditAsync(string folder, Note existing, string rawText, CancellationToken ct = default)
    {
        var result = await compressor.CompressAsync(rawText, ct);
        var note = existing with
        {
            Title = result.Title,
            RawContent = rawText,
            CompressedContent = result.CompressedContent,
            Tags = result.Tags,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var path = await noteStore.SaveAsync(folder, note, ct);
        note = note with { FilePath = path };
        await vectorIndex.UpsertAsync(folder, note, await embedder.EmbedAsync(note.CompressedContent, ct), ct);

        await events.PublishAsync(new NoteEdited(folder, note, result, new List<string>()), ct);
        await events.PublishAsync(new NotesChanged(), ct);
        return note;
    }

    // Import: kazdy tekst przez AddAsync jako FromImport (handlery pomijaja wykrywanie
    // sprzecznosci - N wywolan LLM juz jest, 2N byloby za drogie), luki domykane raz na koniec.
    public async Task<ImportResult> ImportAsync(string folder, IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var count = 0;
        foreach (var text in texts.Select(t => t.Trim()).Where(t => t.Length > 0))
        {
            await AddAsync(folder, text, fromImport: true, ct: ct);
            count++;
        }

        var notices = new List<string>();
        if (count > 0)
            await events.PublishAsync(new ImportCompleted(folder, count, notices), ct);

        return new ImportResult(count, notices);
    }

    // Zapis + embedding + upsert bez kompresji: pin, tagi, rodzic.
    public async Task<Note> ReindexAsync(string folder, Note note, CancellationToken ct = default)
    {
        var path = await noteStore.SaveAsync(folder, note, ct);
        note = note with { FilePath = path };
        await vectorIndex.UpsertAsync(folder, note, await embedder.EmbedAsync(note.CompressedContent, ct), ct);

        await events.PublishAsync(new NoteReindexed(folder, note), ct);
        await events.PublishAsync(new NotesChanged(), ct);
        return note;
    }

    // Przeniesienie miedzy folderami (plik + wpis w indeksie); ten sam folder = ReindexAsync.
    // Walidacja (rodzic istnieje, brak cykli, podnotatki) zostaje u wywolujacego - UI i agent
    // maja rozne reguly i rozne komunikaty.
    public async Task<Note> MoveAsync(string fromFolder, string toFolder, Note note, CancellationToken ct = default)
    {
        if (fromFolder == toFolder)
            return await ReindexAsync(fromFolder, note, ct);

        var newPath = await noteStore.MoveAsync(fromFolder, toFolder, note, ct);
        note = note with { FilePath = newPath };
        await vectorIndex.DeleteNoteAsync(fromFolder, note.Id, ct);
        await vectorIndex.UpsertAsync(toFolder, note, await embedder.EmbedAsync(note.CompressedContent, ct), ct);

        await events.PublishAsync(new NoteReindexed(toFolder, note), ct);
        await events.PublishAsync(new NotesChanged(), ct);
        return note;
    }

    public async Task TrashAsync(string folder, Note note, CancellationToken ct = default)
    {
        await vectorIndex.DeleteNoteAsync(folder, note.Id, ct);
        await noteStore.MoveToTrashAsync(folder, note.FilePath, ct);

        await events.PublishAsync(new NoteTrashed(folder, note), ct);
        await events.PublishAsync(new NotesChanged(), ct);
    }

    public async Task<TrashedNote> RestoreAsync(string trashPath, CancellationToken ct = default)
    {
        var restored = await noteStore.RestoreFromTrashAsync(trashPath, ct);
        await vectorIndex.UpsertAsync(restored.OriginalFolder, restored.Note, await embedder.EmbedAsync(restored.Note.CompressedContent, ct), ct);

        await events.PublishAsync(new NoteRestored(restored), ct);
        await events.PublishAsync(new NotesChanged(), ct);
        return restored;
    }

    public async Task PurgeAsync(string trashPath, CancellationToken ct = default)
    {
        await noteStore.PurgeTrashAsync(trashPath, ct);
        await events.PublishAsync(new NotePurged(trashPath), ct);
    }

    public async Task<bool> CreateFolderAsync(string name, CancellationToken ct = default)
    {
        var created = await vectorIndex.CreateFolderAsync(name, ct);
        if (created)
        {
            await events.PublishAsync(new FolderCreated(name), ct);
            await events.PublishAsync(new NotesChanged(), ct);
        }
        return created;
    }

    // Trwale (bez kosza) - patrz decyzja w PLAN.md.
    public async Task DeleteFolderAsync(string folder, CancellationToken ct = default)
    {
        await vectorIndex.DeleteFolderAsync(folder, ct);
        await noteStore.DeleteFolderAsync(folder, ct);

        await events.PublishAsync(new FolderDeleted(folder), ct);
        await events.PublishAsync(new NotesChanged(), ct);
    }
}
