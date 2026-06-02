using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using PhotoSelector.App.ViewModels;

namespace PhotoSelector.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }

    private async void OpenDirectoryButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || !StorageProvider.CanOpen)
        {
            return;
        }

        var directories = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open photo directory",
            AllowMultiple = false,
        });

        var directory = directories.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(directory))
        {
            viewModel.LoadDirectory(directory);
        }
    }

    private void ScanButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (Directory.Exists(viewModel.ProjectDirectory))
        {
            viewModel.LoadDirectory(viewModel.ProjectDirectory);
        }
    }
}
