namespace Zettelkasten.App.Services;

public interface IDialogService
{
    bool Confirm(string title, string message);
    /// <summary>Lets the user search and pick a note. Returns its ID or null when cancelled.</summary>
    string? PickNote(string title, string? excludeId);
    string? PickSaveFile(string title, string defaultFileName, string filter);
    void ShowError(string title, string message);
}
