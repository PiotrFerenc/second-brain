using Avalonia.Controls;
using SecondBrain.Desktop.ViewModels;

namespace SecondBrain.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            if (DataContext is MainViewModel vm)
                await vm.LoadFoldersCommand.ExecuteAsync(null);
        };
    }
}
