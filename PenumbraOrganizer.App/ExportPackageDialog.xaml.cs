namespace PenumbraOrganizer.App;

using System.Windows;
using PenumbraOrganizer.App.ViewModels;
using PenumbraOrganizer.Core.Models;

public partial class ExportPackageDialog : Window
{
    private readonly MainViewModel _viewModel;

    public ExportPackageDialog(InventoryExportResult exportResult, MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
    }

    private void OpenExportFolder_Click(object sender, RoutedEventArgs e)
        => _viewModel.OpenExportFolderCommand.Execute(null);

    private void CopyPrompt_Click(object sender, RoutedEventArgs e)
        => _viewModel.CopyMasterPromptCommand.Execute(null);

    private void CopyInventoryPath_Click(object sender, RoutedEventArgs e)
        => _viewModel.CopyInventoryFilePathCommand.Execute(null);

    private void Done_Click(object sender, RoutedEventArgs e)
        => Close();
}
