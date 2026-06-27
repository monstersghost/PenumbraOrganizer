namespace PenumbraOrganizer.App.Dialogs;

using System.Windows;

public partial class ApplyTestConfirmationDialog : Window
{
    public ApplyTestConfirmationDialog(
        string title,
        string heading,
        string description,
        string confirmationText,
        string confirmButtonText)
    {
        InitializeComponent();
        DialogTitle = title;
        Heading = heading;
        Description = description;
        ConfirmationText = confirmationText;
        ConfirmButtonText = confirmButtonText;
        DataContext = this;
    }

    public string DialogTitle { get; }

    public string Heading { get; }

    public string Description { get; }

    public string ConfirmationText { get; }

    public string ConfirmButtonText { get; }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
