using System.Windows.Controls;
using ReadestStats.ViewModels;

namespace ReadestStats.Views;

public partial class NotesView : UserControl
{
    public NotesView() => InitializeComponent();

    private void Notes_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Random/history may select a note outside the narrowed list. WPF then removes
        // the visual selection; that coercion must not clear the independent reader.
        if (sender is not ListBox || DataContext is not MainViewModel viewModel) return;
        if (e.AddedItems.OfType<NoteRow>().FirstOrDefault() is { } note
            && !ReferenceEquals(viewModel.SelectedNote, note))
            viewModel.SelectedNote = note;
    }
}
