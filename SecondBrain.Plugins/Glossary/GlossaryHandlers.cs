using SecondBrain.Core;

namespace SecondBrain.Plugins.Glossary;

// Definicje ("X to Y") zlapane przy kompresji trafiaja do slownika - globalnie, nie per folder.
// Czysty backend: dziala tak samo w Desktopie i CLI. Handler zdarzenia nigdy nie czeka na
// watek UI (w CLI nie ma petli Avalonii - InvokeAsync wisialby w nieskonczonosc).
public sealed class GlossaryOnNoteAdded(INoteStore noteStore) : IEventHandler<NoteAdded>, IEventHandler<NoteEdited>
{
    public Task HandleAsync(NoteAdded e, CancellationToken ct = default) => SaveAsync(e.Result, ct);
    public Task HandleAsync(NoteEdited e, CancellationToken ct = default) => SaveAsync(e.Result, ct);

    private async Task SaveAsync(CompressionResult result, CancellationToken ct)
    {
        foreach (var def in result.Definitions ?? [])
            await noteStore.SaveGlossaryEntryAsync(def.Term, def.Definition, result.Title, ct);
    }
}
