using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SecondBrain.Core;
using SecondBrain.Infrastructure;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Plugins.Tags;

// Tagi: chipy pod tytulem notatki (klik = filtr drzewa), baner aktywnego filtra nad drzewem,
// auto-tagowanie z sasiadow przy zapisie, czyszczenie tagow (LLM grupuje duplikaty, TagMerger
// scala) z narzedziami agenta find_duplicate_tags/merge_tags. Wlasna sekcja configu
// "TagCleaning" i wlasny named HttpClient, jak kazdy provider (PLAN.md sekcja 3).
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

        services.Configure<TagCleaningOptions>(config.GetSection("TagCleaning"));
        services.AddHttpClient("TagCleaning", (sp, client) =>
            HttpClientHeaders.Apply(client, sp.GetRequiredService<IOptions<TagCleaningOptions>>().Value));
        services.AddSingleton<TagCleaner>();
        services.AddSingleton<TagMerger>();
        services.AddSingleton<IAgentTool, FindDuplicateTagsTool>();
        services.AddSingleton<IAgentTool, MergeTagsTool>();
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
