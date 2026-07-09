namespace PenumbraOrganizer.App.Dialogs;

using System.Windows;
using PenumbraOrganizer.Core.Models;

public partial class PenumbraObservationDialog : Window
{
    public PenumbraObservationDialog(bool includesOrganizationCleanup = false)
    {
        InitializeComponent();

        if (includesOrganizationCleanup)
        {
            HeaderText.Text = "Open Penumbra and check whether the new virtual folder appears, and whether the pruned folder(s) disappeared from the mod list.";
            DisclaimerText.Text = "This observation is stored as diagnostic evidence only. It confirms whether Penumbra's own UI actually reflects the folder cleanup this app just applied.";

            AppearedImmediatelyButton.Content = "Reflected immediately";
            AppearedAfterReloadButton.Content = "Reflected after reload/restart";
            DidNotAppearButton.Content = "Not reflected yet";
        }
    }

    public PenumbraUiObservationStatus? Observation { get; private set; }

    private void AppearedImmediately_Click(object sender, RoutedEventArgs e)
        => SetResult(PenumbraUiObservationStatus.AppearedImmediately);

    private void AppearedAfterReload_Click(object sender, RoutedEventArgs e)
        => SetResult(PenumbraUiObservationStatus.AppearedAfterReloadOrRestart);

    private void DidNotAppear_Click(object sender, RoutedEventArgs e)
        => SetResult(PenumbraUiObservationStatus.DidNotAppear);

    private void NotCheckedYet_Click(object sender, RoutedEventArgs e)
        => SetResult(PenumbraUiObservationStatus.NotCheckedYet);

    private void SetResult(PenumbraUiObservationStatus status)
    {
        Observation = status;
        DialogResult = true;
    }
}
