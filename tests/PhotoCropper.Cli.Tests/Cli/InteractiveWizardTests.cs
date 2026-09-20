using PhotoCropper.Cli.Services;
using PhotoCropper.TestHelpers;
using Spectre.Console.Testing;

namespace PhotoCropper.Cli.Tests.Cli;

public sealed class InteractiveWizardTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _scanFile;

    public InteractiveWizardTests()
    {
        _tempDir = TestImageFactory.CreateTempDirectory("InteractiveWizardTests");
        _scanFile = Path.Combine(_tempDir, "scan01.jpg");
        TestImageFactory.CreateStandardTwoPhotoScan(_scanFile);
    }

    [Fact]
    public void Run_NullConsole_ShouldThrowArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => InteractiveWizard.Run(null!));
    }

    [Fact]
    public void Run_CompleteFlowWithDirectory_AcceptsDefaultsAndReturnsOptions()
    {
        using var console = new TestConsole();
        console.Profile.Capabilities.Interactive = true;

        // 1. Input path
        console.Input.PushTextWithEnter(_tempDir);
        // 1b. Recursive confirmation prompt (default: false)
        console.Input.PushKey(ConsoleKey.Enter);

        // 2. Output directory prompt (default: <tempDir>/cropped)
        console.Input.PushKey(ConsoleKey.Enter);

        // 3. Format selection (default: JPEG)
        console.Input.PushKey(ConsoleKey.Enter);
        // 3b. JPEG Quality (default: 100)
        console.Input.PushKey(ConsoleKey.Enter);

        // 4. Enhancements (AutoOrient, RestoreColors, RemoveDust - defaults: yes)
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);

        // 5. Detection profile (default: Standard)
        console.Input.PushKey(ConsoleKey.Enter);
        // 5b. Min expected photos (default: 1)
        console.Input.PushKey(ConsoleKey.Enter);
        // 5c. Max expected photos (default: unlimited)
        console.Input.PushKey(ConsoleKey.Enter);
        // 5d. AutoTune toggle (default: false)
        console.Input.PushKey(ConsoleKey.Enter);

        // 6a. Add EXIF metadata (default: false)
        console.Input.PushKey(ConsoleKey.Enter);
        // 6b. Filename pattern (default: {original}_{index})
        console.Input.PushKey(ConsoleKey.Enter);

        // 7. CPU worker threads (default)
        console.Input.PushKey(ConsoleKey.Enter);

        // 8. Final confirmation (default: yes)
        console.Input.PushKey(ConsoleKey.Enter);

        var result = InteractiveWizard.Run(console);

        result.ShouldNotBeNull();
        result.Inputs.ShouldContain(_tempDir);
        result.Format.ShouldBe("JPEG");
        result.JpegQuality.ShouldBe(100);
        result.AutoOrient.ShouldBeTrue();
        result.RestoreColors.ShouldBeTrue();
        result.RemoveDust.ShouldBeTrue();
        result.Tolerance.ShouldBe(25.0);
        result.MinAreaFactor.ShouldBe(0.25);
        result.MinExpectedPhotos.ShouldBe(1);
        result.MaxExpectedPhotos.ShouldBe(int.MaxValue);
        result.AutoTune.ShouldBeFalse();
        result.FileNamePattern.ShouldBe("{original}_{index}");
        console.Output.ShouldContain("Interactive Batch Extractor Wizard");
        console.Output.ShouldContain("Batch Job Configuration Summary");
    }

    [Fact]
    public void Run_UserCancelsAtFinalConfirmation_ShouldReturnNull()
    {
        using var console = new TestConsole();
        console.Profile.Capabilities.Interactive = true;

        // 1. Input path
        console.Input.PushTextWithEnter(_tempDir);
        // Recursive?
        console.Input.PushKey(ConsoleKey.Enter);

        // 2. Output directory
        console.Input.PushKey(ConsoleKey.Enter);

        // 3. Format & quality
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);

        // 4. Enhancements
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);

        // 5. Detection profile, MinExpectedPhotos, MaxExpectedPhotos, AutoTune
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);

        // 6. EXIF & Pattern
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);

        // 7. Performance
        console.Input.PushKey(ConsoleKey.Enter);

        // 8. Final confirmation -> NO ('n')
        console.Input.PushTextWithEnter("n");

        var result = InteractiveWizard.Run(console);

        result.ShouldBeNull();
    }

    [Fact]
    public void Run_CustomSettings_SetsPngFormatAndCustomParameters()
    {
        using var console = new TestConsole();
        console.Profile.Capabilities.Interactive = true;

        // 1. Input path
        console.Input.PushTextWithEnter(_scanFile);

        // 2. Output directory (custom)
        string customOut = Path.Combine(_tempDir, "custom_out");
        console.Input.PushTextWithEnter(customOut);

        // 3. Format selection: Down arrow to PNG, then Enter
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Enter);

        // 4. Enhancements: Disable auto-orient ('n'), keep colors ('y'), disable dust ('n')
        console.Input.PushTextWithEnter("n");
        console.Input.PushTextWithEnter("y");
        console.Input.PushTextWithEnter("n");

        // 5. Detection profile: Down arrow to Sensitive (index 1), then Enter
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Enter);
        // Min expected photos: 2
        console.Input.PushTextWithEnter("2");
        // Max expected photos: 4
        console.Input.PushTextWithEnter("4");
        // Enable AutoTune ('y')
        console.Input.PushTextWithEnter("y");

        // 6a. Embed EXIF ('y')
        console.Input.PushTextWithEnter("y");
        // Year
        console.Input.PushTextWithEnter("1978");
        // Date
        console.Input.PushTextWithEnter("1978-08-20");
        // Description
        console.Input.PushTextWithEnter("Summer Vacation");

        // 6b. Pattern: Since Year is set to 1978, top choice is {year}_{original}_{index:02}
        console.Input.PushKey(ConsoleKey.Enter);

        // 7. Performance: 2 threads
        console.Input.PushTextWithEnter("2");

        // 8. Final confirmation: Yes
        console.Input.PushKey(ConsoleKey.Enter);

        var result = InteractiveWizard.Run(console);

        result.ShouldNotBeNull();
        result.Inputs.ShouldContain(_scanFile);
        result.OutputDirectory.ShouldBe(customOut);
        result.Format.ShouldBe("PNG");
        result.AutoOrient.ShouldBeFalse();
        result.RestoreColors.ShouldBeTrue();
        result.RemoveDust.ShouldBeFalse();
        result.Tolerance.ShouldBe(35.0);
        result.MinAreaFactor.ShouldBe(0.15);
        result.MinExpectedPhotos.ShouldBe(2);
        result.MaxExpectedPhotos.ShouldBe(4);
        result.AutoTune.ShouldBeTrue();
        result.FileNamePattern.ShouldBe("{year}_{original}_{index:02}");
        result.Year.ShouldBe(1978);
        result.Date.ShouldBe("1978-08-20");
        result.Description.ShouldBe("Summer Vacation");
        result.Threads.ShouldBe(2);
        console.Output.ShouldContain("1978_scan01_01.png");
    }

    [Fact]
    public void Run_InvalidDate_RePromptsUntilValidDateEntered()
    {
        using var console = new TestConsole();
        console.Profile.Capabilities.Interactive = true;

        console.Input.PushTextWithEnter(_scanFile);
        console.Input.PushKey(ConsoleKey.Enter); // Output dir
        console.Input.PushKey(ConsoleKey.Enter); // Format JPEG
        console.Input.PushKey(ConsoleKey.Enter); // Quality 100
        console.Input.PushKey(ConsoleKey.Enter); // AutoOrient
        console.Input.PushKey(ConsoleKey.Enter); // Colors
        console.Input.PushKey(ConsoleKey.Enter); // Dust
        console.Input.PushKey(ConsoleKey.Enter); // Profile
        console.Input.PushKey(ConsoleKey.Enter); // Min photos
        console.Input.PushKey(ConsoleKey.Enter); // Max photos
        console.Input.PushKey(ConsoleKey.Enter); // AutoTune
        console.Input.PushTextWithEnter("y");    // Embed EXIF
        console.Input.PushKey(ConsoleKey.Enter); // Skip Year
        console.Input.PushTextWithEnter("not-a-valid-date"); // Invalid date input
        console.Input.PushTextWithEnter("1995-12-25");        // Valid date input
        console.Input.PushKey(ConsoleKey.Enter); // Skip Description
        console.Input.PushKey(ConsoleKey.Enter); // Pattern
        console.Input.PushKey(ConsoleKey.Enter); // Threads
        console.Input.PushKey(ConsoleKey.Enter); // Confirm

        var result = InteractiveWizard.Run(console);

        result.ShouldNotBeNull();
        result.Date.ShouldBe("1995-12-25");
        console.Output.ShouldContain("Date must be in valid format");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
