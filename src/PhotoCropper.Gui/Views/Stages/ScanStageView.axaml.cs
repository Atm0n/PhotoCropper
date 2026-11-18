using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Input;
using PhotoCropper.Gui.ViewModels;

namespace PhotoCropper.Gui.Views.Stages;

internal sealed partial class ScanStageView : UserControl
{
    public ScanStageView()
    {
        InitializeComponent();
    }

    /// <summary>Outlines the photos in the same colour as the Crops stage's boxes.</summary>
    public void SetBoxColor(Color color) => scanPreview.BoxBrush = new SolidColorBrush(color);

    private void Preview_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ScanStageViewModel { SelectedScan: { } row } vm) vm.OpenScanCommand.Execute(row);
    }

    private void ScanRow_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ScanStageViewModel vm && sender is Control { DataContext: ScanRowViewModel row })
        {
            vm.OpenScanCommand.Execute(row);
        }
    }

    /// <summary>Keeps the selected scan (e.g. one that just arrived) in view in the list.</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ScanStageViewModel vm)
        {
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(ScanStageViewModel.SelectedScan) && vm.SelectedScan is { } row)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => lstScans.ScrollIntoView(row), Avalonia.Threading.DispatcherPriority.Background);
                }
            };
        }
    }
}
