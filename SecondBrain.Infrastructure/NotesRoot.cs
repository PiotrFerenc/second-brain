using Microsoft.Extensions.Options;

namespace SecondBrain.Infrastructure;

// Jedno miejsce wyliczania katalogu notatek (wczesniej skopiowane w FileNoteStore,
// GitBackedNoteStore, FileAgentSessionStore i FileVectorIndex) - kazdy magazyn dostaje
// te klase w konstruktorze zamiast liczyc sciezke sam.
public sealed class NotesRoot(IOptions<StorageOptions> options)
{
    public string Path { get; } = string.IsNullOrWhiteSpace(options.Value.NotesRootPath)
        ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SecondBrain", "notes")
        : options.Value.NotesRootPath;
}
