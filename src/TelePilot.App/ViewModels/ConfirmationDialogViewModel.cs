namespace TelePilot.App.ViewModels;

public sealed record ConfirmationDialogViewModel(
    string Title,
    string Message,
    string ConfirmButtonText,
    string CancelButtonText);
