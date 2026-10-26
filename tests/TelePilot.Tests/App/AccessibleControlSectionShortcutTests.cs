using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using TelePilot.App.Controls;

namespace TelePilot.Tests.App;

public sealed class AccessibleControlSectionShortcutTests
{
    [AvaloniaFact]
    public void TextEditor_ParentSectionShortcutKeepsChosenFocusAndDoesNotEditText()
    {
        AccessibleTextBox editor = new() { Text = "Nie zmieniaj tekstu" };

        VerifyParentSectionShortcuts(editor, editor.FocusEditor, () => Assert.Equal("Nie zmieniaj tekstu", editor.Text));
    }

    [AvaloniaFact]
    public void ComboBox_ParentSectionShortcutKeepsChosenFocusAndSelection()
    {
        AccessibleComboBox selector = new() { ItemsSource = new[] { "Pierwszy", "Drugi" }, SelectedItem = "Pierwszy" };

        VerifyParentSectionShortcuts(selector, selector.FocusSelector, () => Assert.Equal("Pierwszy", selector.SelectedItem));
    }

    private static void VerifyParentSectionShortcuts(Control control, Func<bool> focusControl, Action verifyValue)
    {
        Button sectionHeader = new() { Content = "Sekcja wybrana przez okno" };
        Button ordinaryTabTarget = new() { Content = "Zwykły następny element" };
        if (control is AccessibleTextBox text)
        {
            text.NextTabTarget = ordinaryTabTarget;
            text.PreviousTabTarget = ordinaryTabTarget;
        }
        else if (control is AccessibleComboBox combo)
        {
            combo.NextTabTarget = ordinaryTabTarget;
            combo.PreviousTabTarget = ordinaryTabTarget;
        }

        Window window = new()
        {
            Content = new StackPanel { Children = { sectionHeader, control, ordinaryTabTarget } },
            Width = 500,
            Height = 250,
        };
        int handledShortcuts = 0;
        window.AddHandler(InputElement.KeyDownEvent, (_, args) =>
        {
            if ((args.Key == Key.Tab && (args.KeyModifiers == KeyModifiers.Control
                    || args.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift)))
                || (args.Key is Key.D1 or Key.D2 or Key.D3 && args.KeyModifiers == KeyModifiers.Control))
            {
                handledShortcuts++;
                args.Handled = true;
                sectionHeader.Focus();
            }
        }, RoutingStrategies.Tunnel);
        try
        {
            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            (Key Key, RawInputModifiers Modifiers)[] shortcuts =
            [
                (Key.Tab, RawInputModifiers.Control),
                (Key.Tab, RawInputModifiers.Control | RawInputModifiers.Shift),
                (Key.D1, RawInputModifiers.Control),
                (Key.D2, RawInputModifiers.Control),
                (Key.D3, RawInputModifiers.Control),
            ];
            foreach (var (key, modifiers) in shortcuts)
            {
                Assert.True(focusControl());
                window.KeyPress(key, modifiers, PhysicalKey.None, null);
                window.KeyRelease(key, modifiers, PhysicalKey.None, null);
                Dispatcher.UIThread.RunJobs();

                Assert.Same(sectionHeader, window.FocusManager?.GetFocusedElement());
                Assert.False(ordinaryTabTarget.IsKeyboardFocusWithin);
                verifyValue();
            }

            Assert.Equal(shortcuts.Length, handledShortcuts);
        }
        finally
        {
            window.Close();
        }
    }
}
