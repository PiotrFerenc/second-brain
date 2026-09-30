namespace SecondBrain.Tests.Support;

// PluginManager czyta i zapisuje jeden wspolny plik (HOME/SecondBrain/plugins.json) - klasy
// testowe, ktore z niego korzystaja, nie moga dzialac rownolegle.
[CollectionDefinition(Name)]
public sealed class PluginStateCollection
{
    public const string Name = "plugins.json";
}
