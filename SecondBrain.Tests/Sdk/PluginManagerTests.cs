using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecondBrain.Plugins.Sdk;
using SecondBrain.Tests.Support;

namespace SecondBrain.Tests.Sdk;

public sealed class FakePluginB : IPlugin
{
    public string Id => "bbb";
    public string Name => "B";
    public string Description => "opis b";
    public void ConfigureServices(IServiceCollection services, IConfiguration config) { PluginLog.Add("configure:bbb"); services.AddSingleton<MarkerB>(); }
    public Task StartAsync(IServiceProvider services, CancellationToken ct) { PluginLog.Add("start:bbb"); return Task.CompletedTask; }
}

public sealed class FakePluginA : IPlugin
{
    public string Id => "aaa";
    public string Name => "A";
    public string Description => "opis a";
    public void ConfigureServices(IServiceCollection services, IConfiguration config) { PluginLog.Add("configure:aaa"); services.AddSingleton<MarkerA>(); }
    public Task StartAsync(IServiceProvider services, CancellationToken ct) { PluginLog.Add("start:aaa"); return Task.CompletedTask; }
}

public sealed class MarkerA;
public sealed class MarkerB;

public static class PluginLog
{
    private static readonly List<string> Entries = [];
    public static void Add(string s) { lock (Entries) Entries.Add(s); }
    public static void Clear() { lock (Entries) Entries.Clear(); }
    public static string[] Snapshot() { lock (Entries) return [.. Entries]; }
}

[Collection(PluginStateCollection.Name)]
public class PluginManagerTests : IDisposable
{
    private static string StatePath => Path.Combine(TestEnvironment.Home, "SecondBrain", "plugins.json");

    public PluginManagerTests()
    {
        PluginLog.Clear();
        if (File.Exists(StatePath))
            File.Delete(StatePath);
    }

    public void Dispose()
    {
        if (File.Exists(StatePath))
            File.Delete(StatePath);
    }

    // Discover skanuje caly asembler testowy - w nim sa tylko dwa powyzsze pluginy.
    private static PluginManager Discover() => PluginManager.Discover(typeof(FakePluginA).Assembly);

    private static void WriteState(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        File.WriteAllText(StatePath, json);
    }

    [Fact]
    public void Discover_FindsPlugins_SortedById()
    {
        Assert.Equal(["aaa", "bbb"], Discover().Plugins.Select(p => p.Id));
    }

    [Fact]
    public void Discover_ExposesMetadata()
    {
        var a = Discover().Plugins[0];
        Assert.Equal("A", a.Name);
        Assert.Equal("opis a", a.Description);
    }

    [Fact]
    public void NoStateFile_AllEnabled() =>
        Assert.All(Discover().Plugins, p => Assert.True(p.IsEnabled));

    [Fact]
    public void StateFile_DisablesListedPlugins()
    {
        WriteState("""{"disabled":["bbb"]}""");
        var plugins = Discover().Plugins;

        Assert.True(plugins.Single(p => p.Id == "aaa").IsEnabled);
        Assert.False(plugins.Single(p => p.Id == "bbb").IsEnabled);
    }

    [Fact]
    public void StateFile_UnknownIds_AreIgnored()
    {
        WriteState("""{"disabled":["nie-istnieje"]}""");
        Assert.All(Discover().Plugins, p => Assert.True(p.IsEnabled));
    }

    [Theory]
    [InlineData("to nie json")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("""{"disabled":null}""")]
    [InlineData("{}")]
    public void CorruptStateFile_MeansAllEnabled(string content)
    {
        WriteState(content);
        Assert.All(Discover().Plugins, p => Assert.True(p.IsEnabled));
    }

    [Fact]
    public void StateFile_IsCaseInsensitiveOnPropertyName()
    {
        WriteState("""{"Disabled":["aaa"]}""");
        Assert.False(Discover().Plugins.Single(p => p.Id == "aaa").IsEnabled);
    }

    [Fact]
    public void SetEnabled_False_PersistsCamelCaseList()
    {
        var manager = Discover();
        manager.SetEnabled("bbb", false);

        using var doc = JsonDocument.Parse(File.ReadAllText(StatePath));
        Assert.Equal(["bbb"], doc.RootElement.GetProperty("disabled").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void SetEnabled_SurvivesRestart()
    {
        Discover().SetEnabled("aaa", false);
        Assert.False(Discover().Plugins.Single(p => p.Id == "aaa").IsEnabled);
    }

    [Fact]
    public void SetEnabled_BackToTrue_RemovesFromList()
    {
        var manager = Discover();
        manager.SetEnabled("aaa", false);
        manager.SetEnabled("aaa", true);

        using var doc = JsonDocument.Parse(File.ReadAllText(StatePath));
        Assert.Empty(doc.RootElement.GetProperty("disabled").EnumerateArray());
    }

    [Fact]
    public void SetEnabled_UnknownId_IsNoOp()
    {
        Discover().SetEnabled("nie-ma", false);
        Assert.False(File.Exists(StatePath));
    }

    [Fact]
    public void Entry_IsEnabledChange_RaisesPropertyChanged()
    {
        var entry = Discover().Plugins[0];
        var raised = new List<string?>();
        entry.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        entry.IsEnabled = false;

        Assert.Contains(nameof(entry.IsEnabled), raised);
    }

    [Fact]
    public void Entry_SetterPersistsImmediately()
    {
        Discover().Plugins.Single(p => p.Id == "bbb").IsEnabled = false;
        Assert.Contains("bbb", File.ReadAllText(StatePath));
    }

    [Fact]
    public void ConfigureServices_OnlyEnabledPluginsRegister()
    {
        WriteState("""{"disabled":["bbb"]}""");
        var services = new ServiceCollection();

        Discover().ConfigureServices(services, new ConfigurationBuilder().Build());

        Assert.Equal(["configure:aaa"], PluginLog.Snapshot());
        using var sp = services.BuildServiceProvider();
        Assert.NotNull(sp.GetService<MarkerA>());
        Assert.Null(sp.GetService<MarkerB>());
    }

    [Fact]
    public void ConfigureServices_RegistersManagerItself()
    {
        var manager = Discover();
        var services = new ServiceCollection();
        manager.ConfigureServices(services, new ConfigurationBuilder().Build());

        using var sp = services.BuildServiceProvider();
        Assert.Same(manager, sp.GetRequiredService<PluginManager>());
    }

    [Fact]
    public void ConfigureServices_AllEnabled_CallsInIdOrder()
    {
        Discover().ConfigureServices(new ServiceCollection(), new ConfigurationBuilder().Build());
        Assert.Equal(["configure:aaa", "configure:bbb"], PluginLog.Snapshot());
    }

    [Fact]
    public async Task StartAsync_OnlyEnabledPluginsStart()
    {
        WriteState("""{"disabled":["aaa"]}""");
        await Discover().StartAsync(new ServiceCollection().BuildServiceProvider());
        Assert.Equal(["start:bbb"], PluginLog.Snapshot());
    }
}
