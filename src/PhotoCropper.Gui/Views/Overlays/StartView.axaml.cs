using Avalonia.Controls;
using Avalonia.Input;
using PhotoCropper.Gui.ViewModels;

namespace PhotoCropper.Gui.Views.Overlays;

/// <summary>The start screen (see <see cref="StartViewModel"/>): over the whole window until a project is chosen.</summary>
internal sealed partial class StartView : UserControl
{
    public StartView()
    {
        InitializeComponent();
    }

    private StartViewModel? ViewModel => DataContext as StartViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (ViewModel is { } vm)
        {
            vm.PropertyChanged += (_, args) =>
            {
                // Ready to type the name as soon as the form opens.
                if (args.PropertyName == nameof(StartViewModel.IsCreating) && vm.IsCreating)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        txtProjectName.Focus();
                        txtProjectName.SelectAll();
                    }, Avalonia.Threading.DispatcherPriority.Background);
                }
            };
        }
    }

    private void ProjectName_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ViewModel is { } vm && vm.CreateCommand.CanExecute(null))
        {
            vm.CreateCommand.Execute(null);
            e.Handled = true;
        }
    }
}
