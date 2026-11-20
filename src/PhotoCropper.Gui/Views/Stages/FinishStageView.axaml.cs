using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PhotoCropper.Gui.ViewModels;

namespace PhotoCropper.Gui.Views.Stages;

/// <summary>
/// Finish stage: a grid of photos, and an editor for the selected one. The code here only connects the image viewer
/// (zoom, before/after split, painting) and the editor's keys to the view-model.
/// </summary>
internal sealed partial class FinishStageView : UserControl
{
    public FinishStageView()
    {
        InitializeComponent();

        viewer.StrokeCompleted = points => ViewModel?.AddRepairStroke(points);
        viewer.PropertyChanged += (_, e) =>
        {
            if (e.Property == CompareImageViewer.ZoomProperty) txtZoom.Text = $"{viewer.Zoom * 100:0}%";
        };

        // Ctrl+click / Shift+click on a photo marks it for the bulk actions instead of opening it. Tunnel, so the
        // list box doesn't select (open) the photo first.
        lstPhotos.AddHandler(PointerPressedEvent, OnPhotoPointerPressed, RoutingStrategies.Tunnel);

        // Tunnel: the editor's keys work wherever the focus is inside this view.
        AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnEditorKeyUp, RoutingStrategies.Tunnel);

        DataContextChanged += (_, _) =>
        {
            if (ViewModel is not { } vm) return;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(FinishStageViewModel.SelectedPhoto) || vm.SelectedPhoto == null) return;
                // Keep the photo visible in the filmstrip, and take the keyboard so Space and the arrows work.
                Dispatcher.UIThread.Post(() =>
                {
                    lstFilmstrip.ScrollIntoView(vm.SelectedPhoto);
                    viewer.FocusViewport();
                });
            };
        };
    }

    private FinishStageViewModel? ViewModel => DataContext as FinishStageViewModel;

    private void OnPhotoPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        bool control = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (!control && !shift) return;
        if (ViewModel is not { } vm || (e.Source as Control)?.DataContext is not FinishPhotoViewModel row) return;

        vm.ToggleMark(row, range: shift);
        e.Handled = true;
    }

    private void ZoomIn_Click(object? sender, RoutedEventArgs e) => viewer.ZoomIn();

    private void ZoomOut_Click(object? sender, RoutedEventArgs e) => viewer.ZoomOut();

    private void ZoomFit_Click(object? sender, RoutedEventArgs e) => viewer.FitToView();

    private void ZoomActual_Click(object? sender, RoutedEventArgs e) => viewer.ZoomToActualSize();

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { HasSelection: true } vm || e.KeyModifiers != KeyModifiers.None) return;
        // Sliders and lists use the arrow keys themselves.
        bool focusUsesArrows = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Slider or ListBoxItem or ListBox;

        switch (e.Key)
        {
            case Key.Space:
                viewer.ShowBeforeOnly = true; // hold to see the original
                e.Handled = true;
                break;
            case Key.Left when !focusUsesArrows:
                vm.PreviousPhotoCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Right when !focusUsesArrows:
                vm.NextPhotoCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                vm.ClosePreviewCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void OnEditorKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space) return;
        viewer.ShowBeforeOnly = false;
        e.Handled = true;
    }
}
