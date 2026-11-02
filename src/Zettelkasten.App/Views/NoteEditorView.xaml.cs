using System.Windows;
using System.Windows.Controls;
using Zettelkasten.App.ViewModels;

namespace Zettelkasten.App.Views;

public partial class NoteEditorView : UserControl
{
    private NoteEditorViewModel? _vm;
    private bool _syncing;

    public NoteEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Editor.TextChanged += (_, _) =>
        {
            if (!_syncing && _vm is not null) _vm.Content = Editor.Text;
        };
    }

    public void FocusTitle()
    {
        TitleBox.Focus();
        TitleBox.SelectAll();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null) _vm.NoteLoaded -= OnNoteLoaded;
        _vm = e.NewValue as NoteEditorViewModel;
        if (_vm is not null) _vm.NoteLoaded += OnNoteLoaded;
        OnNoteLoaded(this, EventArgs.Empty);
    }

    private void OnNoteLoaded(object? sender, EventArgs e)
    {
        _syncing = true;
        try
        {
            Editor.Text = _vm?.Content ?? "";
            Editor.Document.UndoStack.ClearAll(); // Ctrl+Z must not bring back the previous note
            Editor.ScrollToHome();
        }
        finally
        {
            _syncing = false;
        }
    }
}
