using SecondBrain.Infrastructure;

namespace SecondBrain.Plugins.Templates;

public record NoteTemplate(string Name, string Content);

// Szablony notatek: <root>/.templates/<nazwa>.md - pliki czytelne i edytowalne przez uzytkownika
// poza aplikacja. Przy pierwszym odczycie zapisuje domyslne. Kod 1:1 z dawnego FileNoteStore
// (zapis domyslnych bez StorageChanged, jak dotad).
public sealed class TemplateStore(NotesRoot notesRoot)
{
    private static readonly (string Name, string Content)[] DefaultTemplates =
    [
        ("Spotkanie", "## Spotkanie\nData: \nUczestnicy: \n\n### Ustalenia\n- \n\n### Kolejne kroki\n- \n"),
        ("Pomysł", "## Pomysł\n\nProblem: \n\nRozwiązanie: \n\nDlaczego to działa: \n"),
        ("Zadanie", "## Zadanie\n\nCel: \n\nKroki:\n1. \n\nTermin: \n"),
    ];

    private string Dir => Path.Combine(notesRoot.Path, ".templates");

    public async Task<IReadOnlyList<NoteTemplate>> ListAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(Dir))
        {
            Directory.CreateDirectory(Dir);
            foreach (var (name, content) in DefaultTemplates)
                await File.WriteAllTextAsync(Path.Combine(Dir, $"{name}.md"), content, ct);
        }

        var templates = new List<NoteTemplate>();
        foreach (var file in Directory.EnumerateFiles(Dir, "*.md").OrderBy(f => f))
            templates.Add(new NoteTemplate(Path.GetFileNameWithoutExtension(file), await File.ReadAllTextAsync(file, ct)));

        return templates;
    }
}
