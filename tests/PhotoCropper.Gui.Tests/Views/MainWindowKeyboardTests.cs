using Avalonia;
using Avalonia.Controls;

namespace PhotoCropper.Gui.Tests.Views;

public sealed class MainWindowKeyboardTests
{
    [Fact]
    public void IsTextInputActiveWhenFocusedElementIsTextBoxReturnsTrue()
    {
        TestAppBuilder.EnsureInitialized();

        var textBox = new TextBox();

        MainWindow.IsTextInputActive(textBox, null).ShouldBeTrue();
    }

    [Fact]
    public void IsTextInputActiveWhenSourceElementIsTextBoxReturnsTrue()
    {
        TestAppBuilder.EnsureInitialized();

        var textBox = new TextBox();

        MainWindow.IsTextInputActive(null, textBox).ShouldBeTrue();
    }

    [Fact]
    public void IsTextInputActiveWhenSourceElementIsChildOfTextBoxReturnsTrue()
    {
        TestAppBuilder.EnsureInitialized();

        var textBox = new CustomTestTextBox();
        var border = new Border();
        textBox.AddVisualChild(border);

        MainWindow.IsTextInputActive(null, border).ShouldBeTrue();
    }

    private sealed class CustomTestTextBox : TextBox
    {
        public void AddVisualChild(Visual child) => VisualChildren.Add(child);
    }

    [Fact]
    public void IsTextInputActiveWhenFocusedElementIsButtonReturnsFalse()
    {
        TestAppBuilder.EnsureInitialized();

        var button = new Button();

        MainWindow.IsTextInputActive(button, button).ShouldBeFalse();
    }

    [Fact]
    public void IsTextInputActiveWhenBothNullReturnsFalse()
    {
        TestAppBuilder.EnsureInitialized();

        MainWindow.IsTextInputActive(null, null).ShouldBeFalse();
    }
}
