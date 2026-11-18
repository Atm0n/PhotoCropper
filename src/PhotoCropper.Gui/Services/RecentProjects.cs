namespace PhotoCropper.Gui.Services;

/// <summary>The list of recently opened projects shown on the start screen: newest first, without duplicates.</summary>
internal static class RecentProjects
{
    /// <summary>How many projects the list keeps.</summary>
    public const int MaxCount = 10;

    /// <summary>The list with <paramref name="folder"/> moved (or added) to the front.</summary>
    public static IReadOnlyList<string> Add(IReadOnlyList<string> list, string folder)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        string normalized = Normalize(folder);
        return [normalized, .. list.Where(f => !SameFolder(f, normalized)).Take(MaxCount - 1)];
    }

    /// <summary>The list without <paramref name="folder"/> (the folder itself is left alone).</summary>
    public static IReadOnlyList<string> Remove(IReadOnlyList<string> list, string folder)
    {
        ArgumentNullException.ThrowIfNull(list);
        return [.. list.Where(f => !SameFolder(f, folder))];
    }

    public static bool SameFolder(string? a, string? b) =>
        a != null && b != null && string.Equals(Normalize(a), Normalize(b), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string Normalize(string folder) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
}
