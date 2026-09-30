using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using SecondBrain.Plugins.Sdk;

namespace SecondBrain.Plugins.Hello;

public sealed class HelloTab : ObservableObject, ITabContribution
{
    public string Id => "hello";
    public string Title => "Hello";
    public TabArea Placement => TabArea.SidebarToolbar;
    public int Order => 100;
    public KeyGesture? Shortcut => null;

    public Control CreateView() => new HelloView();
}
