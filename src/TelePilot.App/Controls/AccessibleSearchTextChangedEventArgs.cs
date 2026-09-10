namespace TelePilot.App.Controls;

/// <summary>
/// Provides the previous and current value of an <see cref="AccessibleTextBox"/>.
/// </summary>
public sealed class AccessibleSearchTextChangedEventArgs : EventArgs
{
    public AccessibleSearchTextChangedEventArgs(string oldText, string newText)
    {
        OldText = oldText;
        NewText = newText;
    }

    public string OldText { get; }

    public string NewText { get; }
}
