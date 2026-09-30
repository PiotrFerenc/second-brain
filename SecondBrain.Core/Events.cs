namespace SecondBrain.Core;

// Szyna zdarzen: jedyny kanal backendowy miedzy rdzeniem (NotePipeline, magazyny) a reakcjami
// na zmiany (slownik, sprzecznosci, luki, backup do gita). Handlery z DI, sekwencyjnie,
// w kolejnosci rejestracji; wyjatek handlera jest logowany i nie przerywa operacji.
public interface IEventHandler<in TEvent>
{
    Task HandleAsync(TEvent e, CancellationToken ct = default);
}

public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent e, CancellationToken ct = default);
}

// Po kompresji, PRZED zapisem - handler moze zmienic Tags (np. auto-tagowanie z sasiadow).
// TagsFromUser: tagi wpisane recznie (nie z kompresji) - handlery nie powinny ich dokladac.
public record NoteCompressed(string Folder, string RawText, CompressionResult Result, IList<string> Tags, bool TagsFromUser, IReadOnlyList<Note> Related);

// Notices: komunikaty dla uzytkownika dopisywane przez handlery (sprzecznosc, domkniete luki) -
// wywolujacy (edytor, agent, CLI) pokazuje je po swojemu.
public record NoteAdded(string Folder, Note Note, CompressionResult Result, IReadOnlyList<Note> Related, bool FromImport, ICollection<string> Notices);
public record NoteEdited(string Folder, Note Note, CompressionResult Result, ICollection<string> Notices);

// Zapis + upsert bez kompresji (pin, tagi, rodzic, przeniesienie).
public record NoteReindexed(string Folder, Note Note);
public record NoteTrashed(string Folder, Note Note);
public record NoteRestored(TrashedNote Restored);
public record NotePurged(string TrashPath);
public record FolderCreated(string Folder);
public record FolderDeleted(string Folder);
public record ImportCompleted(string Folder, int Count, ICollection<string> Notices);

// Zbiorczo po kazdej mutacji notatek - dla tych, ktorzy tylko odswiezaja widok.
public record NotesChanged;

public record SearchCompleted(string Query, bool Answered);

// Cos zmienilo sie na dysku w katalogu notatek (notatka albo magazyn funkcji: .gaps, .glossary,
// .facts, .templates) - Message to gotowy tytul commita backupu.
public record StorageChanged(string Message);
