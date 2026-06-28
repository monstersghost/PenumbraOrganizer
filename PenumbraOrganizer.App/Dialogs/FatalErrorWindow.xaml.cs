namespace PenumbraOrganizer.App.Dialogs;

using System.Windows;

public partial class FatalErrorWindow : Window
{
    public FatalErrorWindow(string summary, string stage, Exception exception, string logsDirectory, string logPath)
    {
        InitializeComponent();
        DataContext = new FatalErrorWindowViewModel(summary, stage, exception, logsDirectory, logPath);
    }

    private void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
        => StartupBootstrapLogger.OpenLogsFolder();

    private void CopyDetails_Click(object sender, RoutedEventArgs e)
        => StartupBootstrapLogger.CopyDetails();

    private void Close_Click(object sender, RoutedEventArgs e)
        => Close();
}

public sealed class FatalErrorWindowViewModel
{
    public FatalErrorWindowViewModel(string summary, string stage, Exception exception, string logsDirectory, string logPath)
    {
        Summary = $"{summary}{Environment.NewLine}{Environment.NewLine}{exception.Message}";
        LogLocationLabel = string.IsNullOrWhiteSpace(logsDirectory)
            ? $"Startup log: {logPath}"
            : $"Logs folder: {logsDirectory}";
        Details = $"Stage: {stage}{Environment.NewLine}{Environment.NewLine}{exception}";
    }

    public string Summary { get; }
    public string LogLocationLabel { get; }
    public string Details { get; }
}
