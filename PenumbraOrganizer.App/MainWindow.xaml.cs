namespace PenumbraOrganizer.App;

using System.Windows;
using PenumbraOrganizer.App.ViewModels;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
