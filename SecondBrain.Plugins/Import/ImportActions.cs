using Avalonia.Controls;
using Avalonia.Platform.Storage;
using SecondBrain.Infrastructure;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Plugins.Import;

// Przycisk w slocie Editor.Toolbar: picker -> lista tekstow -> NotePipeline.ImportAsync do folderu
// wybranego w edytorze. Bez wykrywacza sprzecznosci (N linii = N wywolan LLM, kolejne podwoilyby
// koszt), definicje do slownika i domykanie luk ida przez zdarzenia jak przy pojedynczej notatce.
public abstract class ImportAction(NotePipeline pipeline, IShell shell, IEditorContext editor) : ISlotContribution
{
    public string SlotId => "Editor.Toolbar";
    public abstract int Order { get; }

    protected abstract string Title { get; }
    protected abstract Task<List<string>?> PickAsync(IStorageProvider storage);

    public Control CreateControl()
    {
        var button = new Button { Content = Title, Classes = { "subtleAction" } };
        button.Click += async (_, _) => await ImportAsync();
        return button;
    }

    private async Task ImportAsync()
    {
        var texts = await PickAsync(shell.TopLevel.StorageProvider);
        if (texts is null)
            return;

        var folder = editor.Folder;
        if (folder is null)
        {
            editor.Status = "Wybierz folder przed importem.";
            return;
        }

        editor.IsBusy = true;
        try
        {
            editor.Status = "Importuje...";
            var imported = await pipeline.ImportAsync(folder, texts);
            if (imported.Count == 0)
            {
                editor.Status = "";
                return;
            }

            editor.Status = string.Join(" ", imported.Notices.Prepend($"Zaimportowano notatek: {imported.Count}."));
            await shell.RefreshTreeAsync();
        }
        finally
        {
            editor.IsBusy = false;
        }
    }
}

public sealed class ImportFileAction(NotePipeline pipeline, IShell shell, IEditorContext editor)
    : ImportAction(pipeline, shell, editor)
{
    public override int Order => 10;
    protected override string Title => "Importuj z pliku...";

    protected override async Task<List<string>?> PickAsync(IStorageProvider storage)
    {
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Wybierz plik do importu (kazda linia = nowa notatka)",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Tekst") { Patterns = ["*.txt", "*.md"] }]
        });

        if (files.Count == 0)
            return null;

        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
            lines.Add(line);
        return lines;
    }
}

// Cala tresc pliku = jedna notatka (nie linia po linii jak z pliku) - dla pipeline'u "linia"
// to po prostu jeden tekst do skompresowania, wiec tresc pliku tez pasuje.
public sealed class ImportFolderAction(NotePipeline pipeline, IShell shell, IEditorContext editor)
    : ImportAction(pipeline, shell, editor)
{
    public override int Order => 20;
    protected override string Title => "Importuj z folderu...";

    protected override async Task<List<string>?> PickAsync(IStorageProvider storage)
    {
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Wybierz folder do importu (kazdy plik .txt/.md = nowa notatka)",
            AllowMultiple = false
        });

        var folderPath = folders.Count == 0 ? null : folders[0].TryGetLocalPath();
        if (folderPath is null)
            return null;

        var files = Directory.EnumerateFiles(folderPath)
            .Where(f => f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .Order();

        var contents = new List<string>();
        foreach (var file in files)
            contents.Add(await File.ReadAllTextAsync(file));
        return contents;
    }
}
