using System.Text.Json.Serialization;

namespace SecondBrain.Core;

public record GlossaryTerm(
    [property: JsonPropertyName("term")] string Term,
    [property: JsonPropertyName("definition")] string Definition);

public record CompressionResult(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("content")] string CompressedContent,
    [property: JsonPropertyName("tags")] string[] Tags,
    [property: JsonPropertyName("definitions")] GlossaryTerm[] Definitions);

public record TrashedNote(Note Note, string OriginalFolder, string TrashPath);

public record NoteTemplate(string Name, string Content);

public record AnswerResult(
    [property: JsonPropertyName("answered")] bool Answered,
    [property: JsonPropertyName("answer")] string Answer);

public record KnowledgeGap(string Query, DateTimeOffset AskedAt, string Path);

public record ConflictResult(
    [property: JsonPropertyName("hasConflict")] bool HasConflict,
    [property: JsonPropertyName("conflictingTitle")] string? ConflictingTitle,
    [property: JsonPropertyName("explanation")] string? Explanation);

public record GlossaryEntry(string Term, string Definition, string SourceTitle);

// Sugestia grupy tagow-duplikatow (np. "spotkanie"/"spotkania") do reczengo scalenia.
public record TagGroup(string[] Tags, string SuggestedCanonical);

// Notatka wraz z folderem, w ktorym lezy - zwracana tam gdzie wywolujacy (np. TagMerger)
// musi wiedziec do ktorego pliku wektorow doupsertowac notatke po zmianie na dysku.
public record FolderedNote(string Folder, Note Note);

// Wersjonowanie faktow: gdy wykrywacz sprzecznosci zlapie sprzeczna wartosc dla tego
// samego tematu, obie wersje trafiaja tu jako trwala historia (nie tylko jednorazowe
// ostrzezenie w statusie, ktore latwo przewinac/przegapic).
public record FactVersion(DateTimeOffset RecordedAt, string SourceTitle, string Statement);

public interface ICompressor
{
    Task<CompressionResult> CompressAsync(string rawText, CancellationToken ct = default);
}

public interface IEmbedder
{
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
}

public interface IReranker
{
    Task<IReadOnlyList<ScoredNote>> RerankAsync(string query, IReadOnlyList<ScoredNote> candidates, CancellationToken ct = default);
}

// Osobny krok od wyszukiwania: bierze juz-znalezione notatki i syntetyzuje z nich
// bezposrednia odpowiedz na pytanie. Wyszukiwanie samo w sobie dziala bez tego kroku.
// Answered=false to jawny sygnal "notatki nie zawieraja odpowiedzi", uzywany do logowania
// luk w wiedzy - nie parsujemy tego z wolnego tekstu, LLM zwraca to jako pole JSON.
public interface IAnswerSynthesizer
{
    Task<AnswerResult> SynthesizeAsync(string query, IReadOnlyList<Note> notes, CancellationToken ct = default);
}

// Porownuje tresc nowej notatki z juz istniejacymi (podobnymi) notatkami i probuje
// wykryc sprzecznosc faktow (np. dwie rozne godziny tego samego spotkania).
public interface IConflictDetector
{
    Task<ConflictResult> DetectAsync(string newContent, IReadOnlyList<Note> candidates, CancellationToken ct = default);
}

// Grupuje semantycznie zduplikowane tagi z calej bazy (liczba pojedyncza/mnoga, literowki,
// synonimy) i sugeruje jedna kanoniczna forme na grupe - do recznego scalenia przez usera.
public interface ITagCleaner
{
    Task<IReadOnlyList<TagGroup>> FindDuplicateGroupsAsync(IReadOnlyList<string> allTags, CancellationToken ct = default);
}

public interface INoteStore
{
    Task<string> SaveAsync(string folder, Note note, CancellationToken ct = default);
    Task<Note> LoadAsync(string filePath, CancellationToken ct = default);
    Task<IReadOnlyList<Note>> ListAsync(string folder, CancellationToken ct = default);
    Task DeleteFolderAsync(string folder, CancellationToken ct = default);

    // Przenosi notatke do innego folderu: zapisuje plik w toFolder i usuwa stary
    // (note.FilePath musi wciaz wskazywac na oryginalny plik). ParentId niesiony w `note`
    // idzie tak jak podany - wolajacy odpowiada za jego spojnosc (np. wyzerowanie przy
    // przenoszeniu miedzy folderami, zeby nie wskazywac na rodzica z innego folderu).
    Task<string> MoveAsync(string fromFolder, string toFolder, Note note, CancellationToken ct = default);

    // Kosz: usuniecie notatki przenosi plik do .trash zamiast go kasowac.
    Task<string> MoveToTrashAsync(string folder, string filePath, CancellationToken ct = default);
    Task<IReadOnlyList<TrashedNote>> ListTrashAsync(CancellationToken ct = default);
    Task<TrashedNote> RestoreFromTrashAsync(string trashPath, CancellationToken ct = default);
    Task PurgeTrashAsync(string trashPath, CancellationToken ct = default);

    // Szablony notatek - pliki .md czytelne i edytowalne przez uzytkownika poza aplikacja.
    Task<IReadOnlyList<NoteTemplate>> ListTemplatesAsync(CancellationToken ct = default);

    // Luki w wiedzy: pytania, na ktore RAG jawnie odpowiedzial "notatki tego nie zawieraja".
    Task LogGapAsync(string query, CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgeGap>> ListGapsAsync(CancellationToken ct = default);
    Task ResolveGapAsync(string path, CancellationToken ct = default);

    // Auto-slownik: definicje wylapane przy kompresji, globalne (nie per-folder).
    // Ten sam termin nadpisuje poprzedni wpis (najnowsza definicja wygrywa).
    Task SaveGlossaryEntryAsync(string term, string definition, string sourceTitle, CancellationToken ct = default);
    Task<IReadOnlyList<GlossaryEntry>> ListGlossaryAsync(CancellationToken ct = default);
    Task<bool> DeleteGlossaryEntryAsync(string term, CancellationToken ct = default);

    // Czyszczenie tagow: podmienia kazdy tag z fromTags na toTag we wszystkich notatkach
    // (wszystkie foldery), przepisujac pliki na dysku. Zwraca zaktualizowane notatki wraz
    // z folderem, zeby wywolujacy mogl doupsertowac je do indeksu wektorowego (tagi sa tez w payloadzie).
    Task<IReadOnlyList<FolderedNote>> MergeTagsAsync(string[] fromTags, string toTag, CancellationToken ct = default);

    // Wersjonowanie faktow (patrz FactVersion) - append-only historia per temat.
    Task RecordFactVersionAsync(string subject, string statement, string sourceTitle, CancellationToken ct = default);
    Task<IReadOnlyList<FactVersion>> ListFactHistoryAsync(string subject, CancellationToken ct = default);
}

// Folder = jeden plik w lokalnym indeksie wektorowym. Jedna implementacja (FileVectorIndex) na razie,
// interfejs istnieje żeby Core i Desktop nie zależały od konkretnego klienta bazy.
public interface IVectorIndex
{
    Task<IReadOnlyList<string>> ListFoldersAsync(CancellationToken ct = default);
    Task<bool> CreateFolderAsync(string name, CancellationToken ct = default);
    Task DeleteFolderAsync(string name, CancellationToken ct = default);
    Task UpsertAsync(string folder, Note note, float[] vector, CancellationToken ct = default);
    Task DeleteNoteAsync(string folder, Guid noteId, CancellationToken ct = default);
    Task<IReadOnlyList<ScoredNote>> SearchAsync(string folder, float[] vector, ulong limit, CancellationToken ct = default);
}

// Agent czatowy z dostepem do calego programu przez narzedzia (function calling).
// ConversationState to nieprzezroczysty blob (historia rozmowy + wywolan narzedzi w formacie
// providera) - wywolujacy tylko go przechowuje i oddaje z powrotem, nie zagladajac w srodek.
public record AgentPendingAction(string ToolName, string ArgumentsJson, string Summary);

public record AgentStepResult(
    string ConversationState,
    string? ReplyText,
    AgentPendingAction? PendingAction,
    IReadOnlyList<string> ExecutedActions);

public interface IAgent
{
    // conversationState = "" dla nowej rozmowy.
    Task<AgentStepResult> SendAsync(string conversationState, string userMessage, CancellationToken ct = default);

    // Wywolywane gdy poprzedni wynik mial PendingAction != null - user zaakceptowal/odrzucil w UI.
    Task<AgentStepResult> ConfirmAsync(string conversationState, bool approved, CancellationToken ct = default);
}

// Zarzadzanie sesjami czatu z agentem - kazda rozmowa zapisywana osobno, zeby przetrwac
// restart aplikacji i dalo sie do niej wrocic/przelaczyc. Osobny magazyn od INoteStore
// (to nie notatki), bez git-backupu - historia rozmow nie potrzebuje wersjonowania.
public interface IAgentSessionStore
{
    Task<IReadOnlyList<AgentSession>> ListAsync(CancellationToken ct = default);
    Task SaveAsync(AgentSession session, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

// OCR zrzutu ekranu / obrazka - wyciagniety tekst leci dalej przez zwykly ICompressor,
// tak samo jak notatka wpisana recznie.
public interface IOcrExtractor
{
    Task<string> ExtractTextAsync(byte[] imageBytes, string mimeType, CancellationToken ct = default);
}
