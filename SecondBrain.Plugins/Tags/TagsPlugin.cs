using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecondBrain.Core;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Plugins.Tags;

// Tagi: chipy pod tytulem notatki (klik = filtr drzewa), baner aktywnego filtra nad drzewem,
// auto-tagowanie z sasiadow przy zapisie. Czyszczenie tagow (ITagCleaner/TagMerger) zostaje
// w Infrastructure, bo uzywa go agent (PLAN-AGENT-PLUGINS.md P2).
public sealed class TagsPlugin : IPlugin
{
    public string Id => "tags";
    public string Name => "Tagi";
    public string Description => "Chipy tagów, filtr drzewa po tagu i auto-tagowanie z podobnych notatek.";

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<TagFilter>();
        services.AddSingleton<ISlotContribution, TagChipsSlot>();
        services.AddSingleton<ISlotContribution, TagFilterBanner>();
        services.AddSingleton<IEventHandler<NoteCompressed>, AutoTagOnCompressed>();
    }
}

// ponytail: tagi sasiadow liczone czestosciowo (bez wag/podobienstwa), max 2 dolozone -
// podmienic na cos madrzejszego gdy prosta czestosc zacznie realnie zawadzac.
public sealed class AutoTagOnCompressed : IEventHandler<NoteCompressed>
{
    public Task HandleAsync(NoteCompressed e, CancellationToken ct = default)
    {
        if (e.TagsFromUser)
            return Task.CompletedTask;

        var extra = e.Related
            .SelectMany(n => n.Tags)
            .Where(t => !e.Tags.Contains(t, StringComparer.OrdinalIgnoreCase))
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .Take(2);

        foreach (var tag in extra)
            e.Tags.Add(tag);

        return Task.CompletedTask;
    }
}
