using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace PhotoCropper.Gui;

internal sealed partial class MainWindow
{
    private bool _rightPanelCollapsed;
    private GridLength _savedRightColumnWidth = new(1, GridUnitType.Star);
    private double _savedRightColumnMinWidth = 550;

    private void BtnToggleRightPanel_Click(object? sender, RoutedEventArgs e)
        => ToggleRightPanel();

    private void ToggleRightPanel()
    {
        _rightPanelCollapsed = !_rightPanelCollapsed;

        if (grdMainWorkspace == null) return;
        var colSplitter = grdMainWorkspace.ColumnDefinitions[1];
        var colRight = grdMainWorkspace.ColumnDefinitions[2];

        if (_rightPanelCollapsed)
        {
            // Save current width before collapsing
            _savedRightColumnWidth = colRight.Width;
            _savedRightColumnMinWidth = colRight.MinWidth;
            colRight.MinWidth = 0;
            colRight.Width = new GridLength(32);

            // Hide the splitter and panel content; leave only the toggle button
            colSplitter.Width = new GridLength(0);
            if (brdRightPanelContent != null) brdRightPanelContent.IsVisible = false;
            if (pnlRightPanelHeader != null) pnlRightPanelHeader.IsVisible = false;
            if (txtGallerySelection != null) txtGallerySelection.IsVisible = false;

            // Flip arrow to point right (expand)
            if (txtToggleRightPanelIcon != null) txtToggleRightPanelIcon.Text = "▶";
        }
        else
        {
            // Restore saved width
            colRight.MinWidth = _savedRightColumnMinWidth;
            colRight.Width = _savedRightColumnWidth;

            colSplitter.Width = GridLength.Auto;
            if (brdRightPanelContent != null) brdRightPanelContent.IsVisible = true;
            if (pnlRightPanelHeader != null) pnlRightPanelHeader.IsVisible = true;

            // Flip arrow to point left (collapse)
            if (txtToggleRightPanelIcon != null) txtToggleRightPanelIcon.Text = "◀";
        }
    }
}
