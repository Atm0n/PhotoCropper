namespace PhotoCropper.Cli.Tests.Cli;

/// <summary>
/// Spectre.Console allows only one live progress display at a time, so test classes that run the batch or
/// restore processor (which show one) must not run in parallel with each other.
/// </summary>
[CollectionDefinition(nameof(ConsoleProgressGroup), DisableParallelization = true)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515", Justification = "xUnit collection definitions must be public")]
public sealed class ConsoleProgressGroup;
