namespace PenumbraOrganizer.App.Dialogs;

using System.Windows;
using PenumbraOrganizer.Core.Models;

public partial class PenumbraObservationDialog : Window
{
    public PenumbraObservationDialog()
    {
        InitializeComponent();
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
