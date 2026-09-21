namespace PhotoCropper.Core.Updates;

public sealed record UpdateCheckResult(
    bool IsUpdateAvailable,
    Version? CurrentVersion,
    Version? LatestVersion,
    string? Tag,
    Uri? ReleaseUri,
    string? ErrorMessage = null)
{
    public static UpdateCheckResult UpToDate(Version currentVersion, Version? latestVersion = null, string? tag = null, Uri? releaseUri = null) =>
        new(false, currentVersion, latestVersion ?? currentVersion, tag, releaseUri);

    public static UpdateCheckResult Available(Version currentVersion, Version latestVersion, string tag, Uri releaseUri) =>
        new(true, currentVersion, latestVersion, tag, releaseUri);

    public static UpdateCheckResult Failed(Version currentVersion, string errorMessage) =>
        new(false, currentVersion, null, null, null, errorMessage);
}
