using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using OpenCvSharp;

namespace PhotoCropper.Trainer.Sources;

/// <summary>A photograph chosen for download.</summary>
internal sealed record PhotoRecord(string MediaId, string Unit, string Title, string Link);

/// <summary>
/// Public-domain photographs from Smithsonian Open Access: CC0 (no copyright restrictions), published in a public
/// AWS S3 bucket. Metadata and images both come from the bucket, never from the museum's own web servers, and
/// politely: a few requests at a time, pausing when the bucket says to slow down, caching what was fetched so a run
/// can be resumed. Only records whose object type is a photograph and whose media is marked CC0 are taken.
/// </summary>
internal sealed class SmithsonianPhotos(HttpClient http, string output, Action<string> log) : IDisposable
{
    public const string Bucket = "https://smithsonian-open-access.s3-us-west-2.amazonaws.com";

    /// <summary>Museums with many photographs: African American History and Culture, the Portrait Gallery, American History, the Archives of American Art.</summary>
    public static readonly IReadOnlyList<string> DefaultUnits = ["nmaahc", "npg", "nmah", "aaa"];

    private static readonly string[] PhotoTypes = ["Photographs", "Photographic prints", "Gelatin silver prints", "Albumen prints", "Snapshots", "Cabinet photographs"];

    private const int Parallelism = 4;
    private const int MaxAttempts = 6;
    private const int MaxConsecutiveFailures = 25;
    private const int SavedLongSide = 1200;

    private readonly SemaphoreSlim _slots = new(Parallelism);
    private readonly Lock _pauseLock = new();
    private DateTime _pausedUntil = DateTime.MinValue;
    private int _consecutiveFailures;

    private string CacheFolder => Path.Combine(output, ".cache");

    /// <summary>Chooses up to <paramref name="count"/> photographs from <paramref name="units"/>, spread across their records.</summary>
    public async Task<List<PhotoRecord>> ChooseAsync(IReadOnlyList<string> units, int count, Rng rng, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(rng);
        var files = new List<(string Unit, string Key)>();
        foreach (string unit in units)
        {
            foreach (string key in await ListAsync($"metadata/edan/{unit}/", ct)) files.Add((unit, key));
        }
        // A random spread of metadata files, so the photos aren't all from one corner of a collection.
        var order = files.OrderBy(_ => rng.NextULong()).ToList();

        var chosen = new List<PhotoRecord>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (unit, key) in order)
        {
            if (chosen.Count >= count) break;
            string text = await GetCachedTextAsync(key, ct);
            foreach (var record in Photographs(text, unit))
            {
                if (chosen.Count >= count) break;
                if (seen.Add(record.MediaId)) chosen.Add(record);
            }
            log($"Looked through {Path.GetFileName(key)} of {unit}: {chosen.Count:N0} photographs chosen so far.");
        }
        return chosen;
    }

    /// <summary>The CC0 photographs in one metadata file (one JSON record per line).</summary>
    internal static IEnumerable<PhotoRecord> Photographs(string metadata, string unit)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        foreach (string line in metadata.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            PhotoRecord? record = null;
            try
            {
                using var json = JsonDocument.Parse(line);
                record = Photograph(json.RootElement, unit);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                // A broken line, or a record shaped differently from the others: skip it.
            }
            if (record != null) yield return record;
        }
    }

    private static PhotoRecord? Photograph(JsonElement root, string unit)
    {
        if (!root.TryGetProperty("content", out var content)) return null;
        if (!content.TryGetProperty("indexedStructured", out var indexed) || !indexed.TryGetProperty("object_type", out var types)) return null;
        if (!Strings(types).Any(t => PhotoTypes.Contains(t, StringComparer.OrdinalIgnoreCase))) return null;
        if (!content.TryGetProperty("descriptiveNonRepeating", out var descriptive)) return null;
        if (!descriptive.TryGetProperty("online_media", out var online) || online.ValueKind != JsonValueKind.Object) return null;
        if (!online.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Array) return null;

        foreach (var item in media.EnumerateArray().Where(m => m.ValueKind == JsonValueKind.Object))
        {
            if (!Text(item, "type").Equals("Images", StringComparison.OrdinalIgnoreCase)) continue;
            if (!item.TryGetProperty("usage", out var usage) || !Text(usage, "access").Equals("CC0", StringComparison.OrdinalIgnoreCase)) continue;
            string id = Text(item, "idsId");
            if (id.Length == 0 || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;
            if (item.TryGetProperty("width", out var w) && item.TryGetProperty("height", out var h) && w.TryGetInt32(out int width) && h.TryGetInt32(out int height))
            {
                // Skip panoramas and strips: prints are rarely more than 2:1.
                double aspect = (double)width / Math.Max(1, height);
                if (aspect < 0.45 || aspect > 2.2 || Math.Min(width, height) < 400) continue;
            }
            string title = descriptive.TryGetProperty("title", out var t) ? Text(t, "content") : string.Empty;
            // The record's address on collections.si.edu, e.g. edanmdm:nmaahc_2011.165.24.
            string record = Text(root, "url");
            return new PhotoRecord(id, unit, title, $"https://collections.si.edu/search/detail/{record}");
        }
        return null;
    }

    /// <summary>A field that is a list of strings in most records and a single string in some.</summary>
    private static IEnumerable<string> Strings(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Array => element.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString() ?? string.Empty),
        JsonValueKind.String => [element.GetString() ?? string.Empty],
        _ => [],
    };

    private static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    /// <summary>Downloads the chosen photographs (shrunk to 1200 px), skipping those already there, and writes CREDITS.csv.</summary>
    /// <returns>How many photos the folder now holds from this list.</returns>
    public async Task<int> DownloadAsync(IReadOnlyList<PhotoRecord> records, Action<int> progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(progress);
        Directory.CreateDirectory(output);
        int done = 0, saved = 0;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var tasks = records.Select(async record =>
        {
            await _slots.WaitAsync(stop.Token);
            try
            {
                string path = Path.Combine(output, FileName(record));
                bool ok;
                try
                {
                    ok = File.Exists(path) || await DownloadOneAsync(record, path, stop.Token);
                }
                catch (Exception ex) when (ex is OpenCVException or IOException)
                {
                    // A file that can't be decoded or saved: skip it, the others go on.
                    log($"Skipped {record.MediaId}: {ex.Message}");
                    ok = false;
                }
                if (ok) Interlocked.Increment(ref saved);
                if (Volatile.Read(ref _consecutiveFailures) >= MaxConsecutiveFailures)
                {
                    log("Too many failures in a row: stopping. Run the same command later to carry on.");
                    await stop.CancelAsync();
                }
            }
            finally
            {
                _slots.Release();
                progress(Interlocked.Increment(ref done));
            }
        }).ToList();

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Stopped after too many failures; what was saved stays.
        }
        WriteCredits(records);
        return saved;
    }

    internal static string FileName(PhotoRecord record) => $"si_{record.MediaId}.jpg";

    private async Task<bool> DownloadOneAsync(PhotoRecord record, string path, CancellationToken ct)
    {
        byte[]? bytes = await GetBytesAsync($"media/{record.Unit}/{record.MediaId}.jpg", ct);
        if (bytes == null) return false;
        using Mat image = Cv2.ImDecode(bytes, ImreadModes.Color);
        if (image.Empty()) return false;
        double scale = Math.Min(1.0, (double)SavedLongSide / Math.Max(image.Width, image.Height));
        using Mat shrunk = new();
        Cv2.Resize(image, shrunk, new OpenCvSharp.Size(Math.Round(image.Width * scale), Math.Round(image.Height * scale)), 0, 0, InterpolationFlags.Area);
        // Written whole, then renamed: a half-written file never counts as downloaded.
        Cv2.ImEncode(".jpg", shrunk, out byte[] jpeg, [new ImageEncodingParam(ImwriteFlags.JpegQuality, 92)]);
        string partial = path + ".part";
        await File.WriteAllBytesAsync(partial, jpeg, ct);
        File.Move(partial, path, overwrite: true);
        return true;
    }

    private void WriteCredits(IReadOnlyList<PhotoRecord> records)
    {
        var csv = new StringBuilder("file,title,museum,licence,source\n");
        foreach (var record in records.Where(r => File.Exists(Path.Combine(output, FileName(r)))))
        {
            csv.Append(CultureInfo.InvariantCulture, $"{FileName(record)},{Csv(record.Title)},{record.Unit},CC0,{record.Link}\n");
        }
        File.WriteAllText(Path.Combine(output, "CREDITS.csv"), csv.ToString());
    }

    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    /// <summary>The keys under <paramref name="prefix"/> (S3 ListObjectsV2, every page).</summary>
    private async Task<List<string>> ListAsync(string prefix, CancellationToken ct)
    {
        var keys = new List<string>();
        string? token = null;
        do
        {
            string query = $"?list-type=2&prefix={Uri.EscapeDataString(prefix)}" + (token == null ? string.Empty : $"&continuation-token={Uri.EscapeDataString(token)}");
            byte[] bytes = await GetBytesAsync(query, ct) ?? throw new IOException($"Could not list {prefix} in the Smithsonian bucket.");
            var xml = XDocument.Parse(Encoding.UTF8.GetString(bytes));
            XNamespace ns = xml.Root!.Name.Namespace;
            keys.AddRange(xml.Root.Elements(ns + "Contents").Select(c => (string?)c.Element(ns + "Key")).OfType<string>().Where(k => k.EndsWith(".txt", StringComparison.Ordinal)));
            token = (string?)xml.Root.Element(ns + "IsTruncated") == "true" ? (string?)xml.Root.Element(ns + "NextContinuationToken") : null;
        }
        while (token != null);
        return keys;
    }

    private async Task<string> GetCachedTextAsync(string key, CancellationToken ct)
    {
        string cached = Path.Combine(CacheFolder, key.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(cached)) return await File.ReadAllTextAsync(cached, ct);
        byte[] bytes = await GetBytesAsync(key, ct) ?? throw new IOException($"Could not fetch {key} from the Smithsonian bucket.");
        Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
        await File.WriteAllBytesAsync(cached, bytes, ct);
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// GET from the bucket with retries. 429 and 503 ("slow down") pause every request, for as long as Retry-After says
    /// or with growing waits. Null for a missing object or when every attempt failed.
    /// </summary>
    private async Task<byte[]?> GetBytesAsync(string pathAndQuery, CancellationToken ct)
    {
        var url = new Uri(Bucket + "/" + pathAndQuery.TrimStart('/'));
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await WaitWhilePausedAsync(ct);
            try
            {
                using var response = await http.GetAsync(url, ct);
                if (response.IsSuccessStatusCode)
                {
                    Interlocked.Exchange(ref _consecutiveFailures, 0);
                    return await response.Content.ReadAsByteArrayAsync(ct);
                }
                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
                {
                    return null; // Not in the bucket (some records' images aren't): skip it.
                }
                if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
                {
                    TimeSpan wait = response.Headers.RetryAfter?.Delta ?? Backoff(attempt);
                    Pause(wait);
                    log($"The server asked to slow down: pausing {wait.TotalSeconds:F0} s.");
                    continue;
                }
            }
            catch (HttpRequestException)
            {
                // A network hiccup: wait and try again.
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timed out: wait and try again.
            }
            await Task.Delay(Backoff(attempt), ct);
        }
        Interlocked.Increment(ref _consecutiveFailures);
        return null;
    }

    private static TimeSpan Backoff(int attempt) => TimeSpan.FromSeconds(Math.Min(120, Math.Pow(2, attempt)));

    private void Pause(TimeSpan wait)
    {
        lock (_pauseLock)
        {
            DateTime until = DateTime.UtcNow + wait;
            if (until > _pausedUntil) _pausedUntil = until;
        }
    }

    public void Dispose() => _slots.Dispose();

    private async Task WaitWhilePausedAsync(CancellationToken ct)
    {
        while (true)
        {
            TimeSpan remaining;
            lock (_pauseLock) remaining = _pausedUntil - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) return;
            await Task.Delay(remaining, ct);
        }
    }
}
