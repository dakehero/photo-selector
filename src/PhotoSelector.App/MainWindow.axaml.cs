using Avalonia.Controls;
using PhotoSelector.App.ViewModels;

namespace PhotoSelector.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }
}
