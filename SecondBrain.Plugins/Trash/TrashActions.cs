using Avalonia.Controls;
using SecondBrain.Core;
using SecondBrain.Infrastructure;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Plugins.Trash;

// Przycisk "Usun" wstawiany w sloty hosta. Po przeniesieniu do kosza odswiezamy drzewo -
// LoadTreeAsync hosta sam czysci SelectedNote, gdy notatki juz nie ma.
public abstract class DeleteAction(INoteStore noteStore, NotePipeline pipeline, IShell shell) : ISlotContribution
{
    public abstract string SlotId { get; }
    public int Order => 0;

    protected abstract NoteItem? Target { get; }

    public Control CreateControl()
    {
        var button = new Button { Content = "Usuń", Classes = { "subtleAction" } };
        button.Click += async (_, _) => await DeleteAsync();
        return button;
    }

    private async Task DeleteAsync()
    {
        var item = Target;
        if (item is null || string.IsNullOrEmpty(item.FilePath) || string.IsNullOrEmpty(item.Folder))
            return;

        await pipeline.TrashAsync(item.Folder, await noteStore.LoadAsync(item.FilePath));
        await shell.RefreshTreeAsync();
    }
}

public sealed class DeleteNoteAction(INoteStore noteStore, NotePipeline pipeline, IShell shell)
    : DeleteAction(noteStore, pipeline, shell)
{
    private readonly IShell _shell = shell;

    public override string SlotId => "Note.Actions";
    protected override NoteItem? Target => _shell.SelectedNote;
}

public sealed class DeleteSearchResultAction(INoteStore noteStore, NotePipeline pipeline, IShell shell)
    : DeleteAction(noteStore, pipeline, shell)
{
    private readonly IShell _shell = shell;

    public override string SlotId => "Search.ResultActions";
    protected override NoteItem? Target => _shell.SelectedSearchResult;
}
