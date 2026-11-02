using System.Windows;
using Microsoft.Win32;
using Zettelkasten.App.Views;
using Zettelkasten.Core.Abstractions;

namespace Zettelkasten.App.Services;

public sealed class DialogService(ISearchService search) : IDialogService
{
    private static Window? Owner => Application.Current?.MainWindow;

    public bool Confirm(string title, string message) =>
        Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public string? PickNote(string title, string? excludeId)
    {
        var picker = new NotePickerWindow(search, excludeId) { Title = title, Owner = Owner };
        return picker.ShowDialog() == true ? picker.SelectedId : null;
    }

    public string? PickSaveFile(string title, string defaultFileName, string filter)
    {
        var dialog = new SaveFileDialog { Title = title, FileName = defaultFileName, Filter = filter, AddExtension = true };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public void ShowError(string title, string message) =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    private static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image) =>
        Owner is { } owner
            ? MessageBox.Show(owner, message, title, buttons, image)
            : MessageBox.Show(message, title, buttons, image);
}
