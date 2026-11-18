using Avalonia.Controls;
using Avalonia.Input;
using PhotoCropper.Gui.ViewModels;

namespace PhotoCropper.Gui.Views.Stages;

internal sealed partial class ScanStageView : UserControl
{
    private int _lastScanCount;

    public ScanStageView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ScanStageViewModel vm)
            {
                vm.PropertyChanged += (_, e) =>
                {
                    // When a scan is added (e.g. right after scanning), scroll to it so the newest scan is in view.
                    if (e.PropertyName == nameof(ScanStageViewModel.Summary))
                    {
                        if (vm.Scans.Count > _lastScanCount)
                        {
                            Avalonia.Threading.Dispatcher.UIThread.Post(scrollScans.ScrollToEnd, Avalonia.Threading.DispatcherPriority.Background);
                        }
                        _lastScanCount = vm.Scans.Count;
                    }
                };
            }
        };
    }

    private void ScanCard_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ScanStageViewModel vm && sender is Control { DataContext: ScanRowViewModel row })
        {
            vm.OpenScanCommand.Execute(row);
        }
    }
}
