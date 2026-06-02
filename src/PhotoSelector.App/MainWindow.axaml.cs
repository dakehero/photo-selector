using System;
using System.Collections.Generic;
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
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (!StorageProvider.CanOpen)
        {
            viewModel.ReportDirectorySelectionFailed();
            return;
        }

        IReadOnlyList<IStorageFolder> directories;
        try
        {
            directories = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Open photo directory",
                AllowMultiple = false,
            });
        }
        catch (Exception ex)
        {
            viewModel.ReportDirectorySelectionFailed($"Could not open the selected directory: {ex.Message}");
            return;
        }

        var directory = directories.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(directory))
        {
            await viewModel.LoadDirectoryAsync(directory);
            return;
        }

        viewModel.ReportDirectorySelectionFailed("The selected directory is not available on the local filesystem.");
    }

    private async void ScanButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (Directory.Exists(viewModel.ProjectDirectory))
        {
            await viewModel.LoadDirectoryAsync(viewModel.ProjectDirectory);
            return;
        }

        viewModel.ReportDirectorySelectionFailed("Choose a real photo directory before scanning.");
    }
}
