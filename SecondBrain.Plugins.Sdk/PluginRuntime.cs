namespace SecondBrain.Plugins.Sdk;

// Kontrolki Avalonii nie maja DI - SlotHost bierze kontrybucje stad. Host ustawia raz po
// BuildServiceProvider. Jedyne miejsce uzycia lokalizatora serwisow w SDK.
public static class PluginRuntime
{
    public static IServiceProvider? Services { get; set; }
}
