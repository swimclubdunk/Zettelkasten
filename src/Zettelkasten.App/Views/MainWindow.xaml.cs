using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Zettelkasten.App.Services;
using Zettelkasten.App.ViewModels;

namespace Zettelkasten.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private bool _closeConfirmed;

    public MainWindow(MainViewModel vm, IThemeService theme)
    {
        InitializeComponent();
        theme.ThemeChanged += (_, _) => EditorView.ApplyTheme();
        DataContext = _vm = vm;
        vm.FocusEditorRequested += (_, _) => EditorView.FocusEditor();
        PreviewKeyDown += OnPreviewKeyDown;
        MouseDown += OnMouseDown;
        Closing += OnClosing;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.K or Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var command = e.ChangedButton switch
        {
            MouseButton.XButton1 => _vm.BackCommand,
            MouseButton.XButton2 => _vm.ForwardCommand,
            _ => null,
        };
        if (command?.CanExecute(null) == true) command.Execute(null);
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                _vm.Search.ClearCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Enter when _vm.Search.Results.Count > 0:
                _ = _vm.OpenAsync(_vm.Search.Results[0].Note.Id);
                EditorView.Editor.Focus();
                e.Handled = true;
                break;
            case Key.Down when ResultsList.Items.Count > 0:
                ResultsList.Focus();
                ResultsList.SelectedIndex = 0;
                (ResultsList.ItemContainerGenerator.ContainerFromIndex(0) as UIElement)?.Focus();
                e.Handled = true;
                break;
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeConfirmed) return;
        e.Cancel = true;
        await _vm.ShutdownAsync();
        if (_vm.Editor.IsDirty &&
            MessageBox.Show(this, "Your latest changes could not be saved. Close anyway and lose them?",
                "Zettelkasten", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        _closeConfirmed = true;
        // The flush may complete synchronously, i.e. while still inside Closing; Close() is illegal there.
        _ = Dispatcher.BeginInvoke(Close);
    }
}
