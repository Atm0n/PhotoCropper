using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Shouldly;
using Xunit;

namespace PhotoCropper.Gui.Tests.Gui;

public sealed class MainWindowKeyboardTests
{
    [Fact]
    public void IsTextInputActive_WhenFocusedElementIsTextBox_ReturnsTrue()
    {
        TestAppBuilder.EnsureInitialized();

        var textBox = new TextBox();

        MainWindow.IsTextInputActive(textBox, null).ShouldBeTrue();
    }

    [Fact]
    public void IsTextInputActive_WhenSourceElementIsTextBox_ReturnsTrue()
    {
        TestAppBuilder.EnsureInitialized();

        var textBox = new TextBox();

        MainWindow.IsTextInputActive(null, textBox).ShouldBeTrue();
    }

    [Fact]
    public void IsTextInputActive_WhenSourceElementIsChildOfTextBox_ReturnsTrue()
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
    public void IsTextInputActive_WhenFocusedElementIsButton_ReturnsFalse()
    {
        TestAppBuilder.EnsureInitialized();

        var button = new Button();

        MainWindow.IsTextInputActive(button, button).ShouldBeFalse();
    }

    [Fact]
    public void IsTextInputActive_WhenBothNull_ReturnsFalse()
    {
        TestAppBuilder.EnsureInitialized();

        MainWindow.IsTextInputActive(null, null).ShouldBeFalse();
    }
}
