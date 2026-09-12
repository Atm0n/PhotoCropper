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
        options.JpegQuality.ShouldBe(90);
        options.Tolerance.ShouldBe(25);
        options.MinAreaFactor.ShouldBe(0.15);
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
}
