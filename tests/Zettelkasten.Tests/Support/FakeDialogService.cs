using Zettelkasten.App.Services;

namespace Zettelkasten.Tests.Support;

public sealed class FakeDialogService : IDialogService
{
    public bool ConfirmResult { get; set; } = true;
    public List<string> Confirmations { get; } = [];
    public string? PickNoteResult { get; set; }
    public string? SaveFileResult { get; set; }
    public List<string> Errors { get; } = [];

    public bool Confirm(string title, string message) { Confirmations.Add(message); return ConfirmResult; }
    public string? PickNote(string title, string? excludeId) => PickNoteResult;
    public string? PickSaveFile(string title, string defaultFileName, string filter) => SaveFileResult;
    public void ShowError(string title, string message) => Errors.Add(message);
}
