using PhotoCropperCli.Parsing;

namespace PhotoCropper.Tests.Cli;

public sealed class CommandLineParserTests
{
    [Fact]
    public void CommandLineParser_DefaultOptions_ShouldHaveSensibleDefaults()
    {
        var options = CommandLineParser.Parse([]);
        Assert.Empty(options.Inputs);
        Assert.Null(options.OutputDirectory);
        Assert.Equal("JPEG", options.Format);
        Assert.Equal(90, options.JpegQuality);
        Assert.Equal(25, options.Tolerance);
        Assert.Equal(0.15, options.MinAreaFactor);
        Assert.Equal(0.90, options.MaxAreaFactor);
        Assert.Equal(20, options.CannyLow);
        Assert.False(options.Recursive);
        Assert.False(options.Verbose);
        Assert.True(options.AutoOrient);
        Assert.True(options.RestoreColors);
        Assert.True(options.RemoveDust);
        Assert.False(options.AutoTune);
        Assert.Null(options.CopyUndetectedDirectory);
        Assert.False(options.NonInteractive);
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

        Assert.Equal(2, options.Inputs.Count);
        Assert.Equal("input1.jpg", options.Inputs[0]);
        Assert.Equal("input2.jpg", options.Inputs[1]);
        Assert.Equal("D:\\Output", options.OutputDirectory);
        Assert.Equal("PNG", options.Format);
        Assert.Equal(95, options.JpegQuality);
        Assert.Equal(35, options.Tolerance);
        Assert.Equal(0.10, options.MinAreaFactor);
        Assert.Equal(0.80, options.MaxAreaFactor);
        Assert.Equal(15, options.CannyLow);
        Assert.Equal(4, options.Threads);
        Assert.True(options.Recursive);
        Assert.True(options.Verbose);
        Assert.True(options.AutoTune);
        Assert.Equal("D:\\Review", options.CopyUndetectedDirectory);
        Assert.True(options.NonInteractive);
        Assert.False(options.AutoOrient);
        Assert.False(options.RestoreColors);
        Assert.False(options.RemoveDust);
    }

    [Fact]
    public void CommandLineParser_PositionalInputs_ShouldBeCollected()
    {
        string[] args = ["scan1.jpg", "scan2.jpg", "scan3.png", "-o", "out_dir"];
        var options = CommandLineParser.Parse(args);

        Assert.Equal(3, options.Inputs.Count);
        Assert.Contains("scan1.jpg", options.Inputs);
        Assert.Contains("scan2.jpg", options.Inputs);
        Assert.Contains("scan3.png", options.Inputs);
        Assert.Equal("out_dir", options.OutputDirectory);
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

        Assert.Equal("out", options.OutputDirectory);
        Assert.Equal("JPEG", options.Format);
        Assert.Equal(85, options.JpegQuality);
        Assert.Equal(30, options.Tolerance);
        Assert.Equal(8, options.Threads);
        Assert.True(options.Recursive);
        Assert.True(options.Verbose);
        Assert.Equal("iso_dir", options.CopyUndetectedDirectory);
        Assert.True(options.NonInteractive);
        Assert.True(options.AutoOrient);
        Assert.True(options.RestoreColors);
        Assert.True(options.RemoveDust);
    }
}
