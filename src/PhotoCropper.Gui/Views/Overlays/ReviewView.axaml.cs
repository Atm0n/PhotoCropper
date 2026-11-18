using Avalonia.Controls;
using Avalonia.Input;
using PhotoCropper.Gui.ViewModels;

namespace PhotoCropper.Gui.Views.Overlays;

/// <summary>Review mode, full window over the editor. The mouse wheel steps through photos; a double click closes it.</summary>
internal sealed partial class ReviewView : UserControl
{
    public ReviewView()
    {
        InitializeComponent();
    }

    private ReviewViewModel? ViewModel => DataContext as ReviewViewModel;

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (ViewModel is not { IsOpen: true } vm) return;
        if (e.Delta.Y > 0) vm.Previous();
        else if (e.Delta.Y < 0) vm.Next();
        e.Handled = true;
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is not { IsOpen: true } vm) return;
        // Same as "Edit in scanner": back to the editor on this photo.
        vm.Close();
        e.Handled = true;
    }
}
