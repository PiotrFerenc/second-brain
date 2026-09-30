using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecondBrain.Core;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Plugins.Backlinks;

// Odnosniki: ktore notatki w tym samym folderze zawieraja [[Tytul]] wybranej notatki.
// Liczone na zadanie przy kazdej zmianie wyboru (skan folderu, bez indeksu - patrz PLAN.md).
public sealed class BacklinksPlugin : IPlugin
{
    public string Id => "backlinks";
    public string Name => "Odnośniki";
    public string Description => "Lista notatek, które linkują do wybranej notatki przez [[Tytuł]].";

    public void ConfigureServices(IServiceCollection services, IConfiguration config) =>
        services.AddSingleton<ISlotContribution, BacklinksSlot>();
}

public sealed class BacklinksSlot(INoteStore noteStore, IShell shell) : ISlotContribution
{
    public string SlotId => "Note.Footer";
    public int Order => 0;

    public Control CreateControl()
    {
        var text = new Run();
        var block = new TextBlock { Classes = { "subtle" }, FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        block.Inlines!.Add(new Run("Odnośniki do tej notatki: "));
        block.Inlines.Add(text);

        async void Reload() => text.Text = await LoadAsync(shell.SelectedNote);

        block.AttachedToVisualTree += (_, _) => { shell.SelectedNoteChanged += Reload; Reload(); };
        block.DetachedFromVisualTree += (_, _) => shell.SelectedNoteChanged -= Reload;
        return block;
    }

    private async Task<string> LoadAsync(NoteItem? selected)
    {
        if (selected is null)
            return "";

        var notes = await noteStore.ListAsync(selected.Folder);
        var referencing = notes
            .Where(n => n.Id != selected.Id &&
                        n.RawContent.Contains($"[[{selected.Title}]]", StringComparison.OrdinalIgnoreCase))
            .Select(n => n.Title)
            .ToList();

        return referencing.Count > 0 ? string.Join(", ", referencing) : "Brak.";
    }
}
