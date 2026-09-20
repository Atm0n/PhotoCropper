using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace PhotoCropper.Gui.Dialogs;

internal sealed partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
        KeyDown += HelpWindow_KeyDown;
    }

    private void HelpWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
