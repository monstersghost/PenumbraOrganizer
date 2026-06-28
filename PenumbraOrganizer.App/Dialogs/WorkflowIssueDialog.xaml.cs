namespace PenumbraOrganizer.App.Dialogs;

using System.Windows;
using System.Windows.Media;

public partial class WorkflowIssueDialog : Window
{
    public WorkflowIssueDialog(WorkflowIssueDialogModel model)
    {
        InitializeComponent();
        Model = model;
        DataContext = new WorkflowIssueDialogViewModel(model);
    }

    public WorkflowIssueDialogModel Model { get; }

    private void Review_Click(object sender, RoutedEventArgs e)
    {
        Model.Result = WorkflowIssueDialogResult.Review;
        DialogResult = true;
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        Model.Result = WorkflowIssueDialogResult.Skip;
        DialogResult = true;
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        Model.Result = WorkflowIssueDialogResult.Continue;
        DialogResult = true;
    }

    private void CopyDetails_Click(object sender, RoutedEventArgs e)
        => Clipboard.SetText(Model.TechnicalDetails);

    private void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
        => StartupBootstrapLogger.OpenLogsFolder();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Model.Result = WorkflowIssueDialogResult.Cancel;
        DialogResult = false;
    }
}

public enum WorkflowIssueSeverity
{
    Warning,
    HardFailure,
}

public enum WorkflowIssueDialogResult
{
    Cancel,
    Review,
    Skip,
    Continue,
}

public sealed class WorkflowIssueDialogModel
{
    public required string Title { get; init; }
    public required string Heading { get; init; }
    public required string Summary { get; init; }
    public required WorkflowIssueSeverity Severity { get; init; }
    public required bool AnyWriteOccurred { get; init; }
    public required IReadOnlyList<string> AffectedRowsOrMods { get; init; }
    public required string TechnicalDetails { get; init; }
    public bool AllowReview { get; init; }
    public bool AllowSkip { get; init; }
    public bool AllowContinue { get; init; }
    public string ReviewLabel { get; init; } = "Review Affected Rows";
    public string SkipLabel { get; init; } = "Skip Affected Rows and Continue";
    public string ContinueLabel { get; init; } = "Continue Anyway";
    public string CancelLabel { get; init; } = "Cancel";
    public WorkflowIssueDialogResult Result { get; set; } = WorkflowIssueDialogResult.Cancel;
}

public sealed class WorkflowIssueDialogViewModel
{
    public WorkflowIssueDialogViewModel(WorkflowIssueDialogModel model)
    {
        Title = model.Title;
        Heading = model.Heading;
        Summary = model.Summary;
        SeverityLabel = model.Severity == WorkflowIssueSeverity.HardFailure ? "Hard failure" : "Reviewable warning";
        SeverityBrush = model.Severity == WorkflowIssueSeverity.HardFailure
            ? new SolidColorBrush(Color.FromRgb(170, 49, 44))
            : new SolidColorBrush(Color.FromRgb(148, 98, 21));
        WriteStateLabel = model.AnyWriteOccurred
            ? "Some data may already have been written."
            : "No live write was confirmed.";
        AffectedSummary = model.AffectedRowsOrMods.Count == 0
            ? "No specific rows were identified."
            : string.Join(Environment.NewLine, model.AffectedRowsOrMods);
        TechnicalDetails = model.TechnicalDetails;
        LogLocationLabel = string.IsNullOrWhiteSpace(StartupBootstrapLogger.LogPath)
            ? $"Logs folder: {StartupBootstrapLogger.LogsDirectory}"
            : $"Latest log: {StartupBootstrapLogger.LogPath}";
        ReviewVisible = model.AllowReview ? Visibility.Visible : Visibility.Collapsed;
        SkipVisible = model.AllowSkip ? Visibility.Visible : Visibility.Collapsed;
        ContinueVisible = model.AllowContinue ? Visibility.Visible : Visibility.Collapsed;
        ReviewLabel = model.ReviewLabel;
        SkipLabel = model.SkipLabel;
        ContinueLabel = model.ContinueLabel;
        CancelLabel = model.CancelLabel;
    }

    public string Title { get; }
    public string Heading { get; }
    public string Summary { get; }
    public string SeverityLabel { get; }
    public Brush SeverityBrush { get; }
    public string WriteStateLabel { get; }
    public string AffectedSummary { get; }
    public string TechnicalDetails { get; }
    public string LogLocationLabel { get; }
    public Visibility ReviewVisible { get; }
    public Visibility SkipVisible { get; }
    public Visibility ContinueVisible { get; }
    public string ReviewLabel { get; }
    public string SkipLabel { get; }
    public string ContinueLabel { get; }
    public string CancelLabel { get; }
}
