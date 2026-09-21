using PhotoCropper.Cli.Parsing;

namespace PhotoCropper.Cli.Tests.Cli;

public sealed class CommandLineParserTests
{
    [Fact]
    public void CommandLineParser_DefaultOptions_ShouldHaveSensibleDefaults()
    {
        var options = CommandLineParser.Parse([]);
        options.Inputs.ShouldBeEmpty();
        options.OutputDirectory.ShouldBeNull();
        options.Format.ShouldBe("JPEG");
        options.JpegQuality.ShouldBe(100);
        options.Tolerance.ShouldBe(25);
        options.MinAreaFactor.ShouldBe(0.25);
        options.MaxAreaFactor.ShouldBe(0.90);
        options.CannyLow.ShouldBe(20);
        options.Recursive.ShouldBeFalse();
        options.Verbose.ShouldBeFalse();
        options.AutoOrient.ShouldBeTrue();
        options.RestoreColors.ShouldBeTrue();
        options.RemoveDust.ShouldBeTrue();
        options.AutoTune.ShouldBeFalse();
        options.CopyUndetectedDirectory.ShouldBeNull();
        options.NonInteractive.ShouldBeFalse();
    }

    [Fact]
    public void CommandLineParser_ParseAllFlags_ShouldSetCorrectOptions()
    {
        string[] args = [
            "-i", "input1.jpg",
            "-i", "input2.jpg",
            "-o", "D:\\Output",
            "-f", "PNG",
            "-q", "95",
            "-t", "35",
            "--min-size", "10",
            "--max-size", "80",
            "--canny-low", "15",
            "-j", "4",
            "-r",
            "-v",
            "--auto-tune",
            "--copy-undetected", "D:\\Review",
            "-y",
            "--no-auto-orient",
            "--no-restore-colors",
            "--no-remove-dust"
        ];

        var options = CommandLineParser.Parse(args);

        options.Inputs.Count.ShouldBe(2);
        options.Inputs[0].ShouldBe("input1.jpg");
        options.Inputs[1].ShouldBe("input2.jpg");
        options.OutputDirectory.ShouldBe("D:\\Output");
        options.Format.ShouldBe("PNG");
        options.JpegQuality.ShouldBe(95);
        options.Tolerance.ShouldBe(35);
        options.MinAreaFactor.ShouldBe(0.10);
        options.MaxAreaFactor.ShouldBe(0.80);
        options.CannyLow.ShouldBe(15);
        options.Threads.ShouldBe(4);
        options.Recursive.ShouldBeTrue();
        options.Verbose.ShouldBeTrue();
        options.AutoTune.ShouldBeTrue();
        options.CopyUndetectedDirectory.ShouldBe("D:\\Review");
        options.NonInteractive.ShouldBeTrue();
        options.AutoOrient.ShouldBeFalse();
        options.RestoreColors.ShouldBeFalse();
        options.RemoveDust.ShouldBeFalse();
    }

    [Fact]
    public void CommandLineParser_PositionalInputs_ShouldBeCollected()
    {
        string[] args = ["scan1.jpg", "scan2.jpg", "scan3.png", "-o", "out_dir"];
        var options = CommandLineParser.Parse(args);

        options.Inputs.Count.ShouldBe(3);
        options.Inputs.ShouldContain("scan1.jpg");
        options.Inputs.ShouldContain("scan2.jpg");
        options.Inputs.ShouldContain("scan3.png");
        options.OutputDirectory.ShouldBe("out_dir");
    }

    [Fact]
    public void CommandLineParser_AlternativeFlagNames_ShouldBeHandled()
    {
        string[] args = [
            "--output", "out",
            "--format", "jpeg",
            "--quality", "85",
            "--tolerance", "30",
            "--threads", "8",
            "--recursive",
            "--verbose",
            "--isolate-undetected", "iso_dir",
            "--yes",
            "--auto-orient",
            "--restore-colors",
            "--remove-dust"
        ];

        var options = CommandLineParser.Parse(args);

        options.OutputDirectory.ShouldBe("out", StringCompareShould.IgnoreCase);
        options.Format.ShouldBe("JPEG");
        options.JpegQuality.ShouldBe(85);
        options.Tolerance.ShouldBe(30);
        options.Threads.ShouldBe(8);
        options.Recursive.ShouldBeTrue();
        options.Verbose.ShouldBeTrue();
        options.CopyUndetectedDirectory.ShouldBe("iso_dir");
        options.NonInteractive.ShouldBeTrue();
        options.AutoOrient.ShouldBeTrue();
        options.RestoreColors.ShouldBeTrue();
        options.RemoveDust.ShouldBeTrue();
    }

    [Fact]
    public void CommandLineParser_NamingPatternAndMetadataFlags_ShouldBeParsed()
    {
        string[] args = [
            "-i", "input.jpg",
            "-p", "{year}_{original}_{index:02}",
            "--year", "1985",
            "--date", "1985-06-15",
            "--desc", "Family vacation"
        ];

        var options = CommandLineParser.Parse(args);

        options.FileNamePattern.ShouldBe("{year}_{original}_{index:02}");
        options.Year.ShouldBe(1985);
        options.Date.ShouldBe("1985-06-15");
        options.Description.ShouldBe("Family vacation");
    }

    [Theory]
    [InlineData("-w")]
    [InlineData("--wizard")]
    [InlineData("--interactive")]
    public void CommandLineParser_WizardFlags_ShouldSetInteractiveTrue(string flag)
    {
        var options = CommandLineParser.Parse([flag]);
        options.Interactive.ShouldBeTrue();
    }

    [Theory]
    [InlineData("--min-photos", "3", 3)]
    [InlineData("--expected-photos", "4", 4)]
    public void CommandLineParser_MinPhotosFlag_ShouldSetMinExpectedPhotos(string flag, string value, int expected)
    {
        var options = CommandLineParser.Parse([flag, value]);
        options.MinExpectedPhotos.ShouldBe(expected);
    }

    [Theory]
    [InlineData("--max-photos", "5", 5)]
    [InlineData("--max-expected-photos", "8", 8)]
    public void CommandLineParser_MaxPhotosFlag_ShouldSetMaxExpectedPhotos(string flag, string value, int expected)
    {
        var options = CommandLineParser.Parse([flag, value]);
        options.MaxExpectedPhotos.ShouldBe(expected);
    }

    [Fact]
    public void CommandLineParser_PrintHelp_ShouldContainCheckUpdateOption()
    {
        using var sw = new StringWriter();
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(sw);
            CommandLineParser.PrintHelp();
            string output = sw.ToString();
            output.ShouldContain("--check-update");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void CommandLineParser_PrintVersion_ShouldContainCurrentVersion()
    {
        using var sw = new StringWriter();
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(sw);
            CommandLineParser.PrintVersion();
            string output = sw.ToString();
            output.ShouldContain("PhotoCropper CLI v");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void CommandLineParser_SensitivityFlag_ShouldMapToTolerance()
    {
        string[] args = ["--sensitivity", "75"];
        var options = CommandLineParser.Parse(args);
        options.Tolerance.ShouldBeInRange(9.0, 11.0);

        string[] shortArgs = ["-s", "100"];
        var shortOptions = CommandLineParser.Parse(shortArgs);
        shortOptions.Tolerance.ShouldBe(4.0);
    }
}
