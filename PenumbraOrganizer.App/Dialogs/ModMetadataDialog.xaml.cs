namespace PenumbraOrganizer.App.Dialogs;

using System.Windows;
using PenumbraOrganizer.App.ViewModels;

public partial class ModMetadataDialog : Window
{
    private readonly ModRowViewModel _row;
    private readonly (string Name, string Author, string Version, string Website, string ModTags,
        string Description, bool Favorite, string LocalTags, string Note) _original;

    public ModMetadataDialog(ModRowViewModel row)
    {
        InitializeComponent();
        _row = row;
        DataContext = row;

        // Snapshot the editable fields so Cancel can revert the live two-way bindings.
        _original = (row.EditName, row.EditAuthor, row.EditVersion, row.EditWebsite, row.EditModTagsText,
            row.EditDescription, row.Favorite, row.EditLocalTagsText, row.EditNote);
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _row.EditName = _original.Name;
        _row.EditAuthor = _original.Author;
        _row.EditVersion = _original.Version;
        _row.EditWebsite = _original.Website;
        _row.EditModTagsText = _original.ModTags;
        _row.EditDescription = _original.Description;
        _row.Favorite = _original.Favorite;
        _row.EditLocalTagsText = _original.LocalTags;
        _row.EditNote = _original.Note;
        DialogResult = false;
    }
}
