using System.Runtime.CompilerServices;

namespace SecondBrain.Tests.Support;

// PluginManager trzyma stan w ~/SecondBrain/plugins.json (static readonly, liczone raz), a git
// wymaga tozsamosci do commitow - przekierowujemy HOME na katalog tymczasowy, zanim cokolwiek
// dotknie tych typow, zeby testy nigdy nie ruszaly prawdziwego profilu uzytkownika.
internal static class TestEnvironment
{
    public static string Home { get; } = Path.Combine(Path.GetTempPath(), "secondbrain-tests-home-" + Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    internal static void Init()
    {
        Directory.CreateDirectory(Home);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(Home, recursive: true); } catch { /* best effort */ }
        };
        Environment.SetEnvironmentVariable("HOME", Home);
        Environment.SetEnvironmentVariable("USERPROFILE", Home);
        Environment.SetEnvironmentVariable("GIT_AUTHOR_NAME", "Test");
        Environment.SetEnvironmentVariable("GIT_AUTHOR_EMAIL", "test@example.com");
        Environment.SetEnvironmentVariable("GIT_COMMITTER_NAME", "Test");
        Environment.SetEnvironmentVariable("GIT_COMMITTER_EMAIL", "test@example.com");
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", "/dev/null");
        Environment.SetEnvironmentVariable("GIT_CONFIG_SYSTEM", "/dev/null");
    }
}
