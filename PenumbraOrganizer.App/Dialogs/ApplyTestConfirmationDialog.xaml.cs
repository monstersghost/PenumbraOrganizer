namespace PenumbraOrganizer.App.Dialogs;

using System.Windows;

public partial class ApplyTestConfirmationDialog : Window
{
    public ApplyTestConfirmationDialog(string confirmationText)
    {
        InitializeComponent();
        ConfirmationText = confirmationText;
        DataContext = this;
    }

    public string ConfirmationText { get; }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
