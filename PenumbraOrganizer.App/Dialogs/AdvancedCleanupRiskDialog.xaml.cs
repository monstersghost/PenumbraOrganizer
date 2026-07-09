namespace PenumbraOrganizer.App.Dialogs;

using System.Windows;

public partial class AdvancedCleanupRiskDialog : Window
{
    public AdvancedCleanupRiskDialog()
    {
        InitializeComponent();
    }

    public bool Confirmed { get; private set; }

    private void AcknowledgeCheckBox_Changed(object sender, RoutedEventArgs e)
        => ConfirmButton.IsEnabled = AcknowledgeCheckBox.IsChecked == true;

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;
}
