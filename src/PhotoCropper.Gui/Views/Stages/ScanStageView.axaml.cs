using Avalonia.Controls;
using Avalonia.Input;
using PhotoCropper.Gui.ViewModels;

namespace PhotoCropper.Gui.Views.Stages;

internal sealed partial class ScanStageView : UserControl
{
    public ScanStageView()
    {
        InitializeComponent();
    }

    private void LstScans_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ScanStageViewModel vm && lstScans.SelectedItem is ScanRowViewModel row)
        {
            vm.OpenScanCommand.Execute(row);
        }
    }
}
