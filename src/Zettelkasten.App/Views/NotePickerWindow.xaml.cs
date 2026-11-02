using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Model;

namespace Zettelkasten.App.Views;

public partial class NotePickerWindow : Window
{
    private readonly ISearchService _search;
    private readonly string? _excludeId;
    private int _version;

    public NotePickerWindow(ISearchService search, string? excludeId)
    {
        _search = search;
        _excludeId = excludeId;
        InitializeComponent();
        Loaded += async (_, _) => { QueryBox.Focus(); await RefreshAsync(); };
    }

    public string? SelectedId { get; private set; }

    private async void QueryBox_TextChanged(object sender, TextChangedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        var version = ++_version;
        var results = await _search.SearchAsync(QueryBox.Text);
        if (version != _version) return;
        ResultsList.ItemsSource = results.Where(r => r.Note.Id != _excludeId).ToList();
        if (ResultsList.Items.Count > 0) ResultsList.SelectedIndex = 0;
    }

    private void QueryBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var delta = e.Key switch { Key.Down => 1, Key.Up => -1, _ => 0 };
        if (delta == 0 || ResultsList.Items.Count == 0) return;
        ResultsList.SelectedIndex = Math.Clamp(ResultsList.SelectedIndex + delta, 0, ResultsList.Items.Count - 1);
        ResultsList.ScrollIntoView(ResultsList.SelectedItem);
        e.Handled = true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsList.SelectedItem is not SearchResult result) return;
        SelectedId = result.Note.Id;
        DialogResult = true;
    }
}
