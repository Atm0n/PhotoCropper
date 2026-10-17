using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace PhotoCropper.Core.Updates;

public static class UpdateCheckService
{
    private static readonly Uri DefaultReleaseUri = new("https://api.github.com/repos/Atm0n/PhotoCropper/releases/latest");
    private static readonly Uri DefaultFallbackDownloadUri = new("https://github.com/Atm0n/PhotoCropper/releases/latest");

    public static Uri DefaultLatestReleaseUri => DefaultReleaseUri;

    public static Version GetCurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(UpdateCheckService).Assembly;
        var ver = assembly.GetName().Version;
        return ver != null ? new Version(ver.Major, ver.Minor, Math.Max(0, ver.Build)) : new Version(0, 0, 0);
    }

    public static bool TryParseTagVersion(string? tag, out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        string trimmed = tag.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        int dashIndex = trimmed.IndexOf('-', StringComparison.Ordinal);
        if (dashIndex >= 0)
        {
            trimmed = trimmed[..dashIndex];
        }

        return Version.TryParse(trimmed, out version);
    }

    public static Task<UpdateCheckResult> CheckForUpdateAsync(
        Version? currentVersion = null,
        CancellationToken cancellationToken = default)
    {
        return CheckForUpdateAsync(currentVersion, DefaultReleaseUri, null, cancellationToken);
    }

    public static async Task<UpdateCheckResult> CheckForUpdateAsync(
        Version? currentVersion,
        Uri requestUri,
        HttpClient? httpClient = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestUri);

        Version activeVersion = currentVersion ?? GetCurrentVersion();

        HttpClient? ownedClient = null;
        HttpClient client = httpClient ?? (ownedClient = CreateDefaultHttpClient());

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Add("User-Agent", "PhotoCropper");
            request.Headers.Add("Accept", "application/vnd.github.v3+json");

            using var response = await client.SendAsync(request, cancellationToken);

            string? tag = null;
            Uri? downloadUri = null;

            if (response.IsSuccessStatusCode)
            {
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                var root = doc.RootElement;

                if (root.TryGetProperty("tag_name", out var tagElem))
                {
                    tag = tagElem.GetString();
                }

                if (root.TryGetProperty("html_url", out var urlElem) && Uri.TryCreate(urlElem.GetString(), UriKind.Absolute, out var parsedUrl))
                {
                    downloadUri = parsedUrl;
                }
            }
            else if (response.Headers.Location != null)
            {
                Uri loc = response.Headers.Location;
                downloadUri = loc.IsAbsoluteUri ? loc : new Uri(requestUri, loc);
                tag = Path.GetFileName(downloadUri.AbsolutePath.TrimEnd('/'));
            }

            downloadUri ??= DefaultFallbackDownloadUri;

            if (TryParseTagVersion(tag, out Version? latestVersion) && latestVersion != null)
            {
                if (latestVersion > activeVersion)
                {
                    return UpdateCheckResult.Available(activeVersion, latestVersion, tag!, downloadUri);
                }

                return UpdateCheckResult.UpToDate(activeVersion, latestVersion, tag, downloadUri);
            }

            return UpdateCheckResult.UpToDate(activeVersion);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or System.Text.Json.JsonException)
        {
            return UpdateCheckResult.Failed(activeVersion, ex.Message);
        }
        finally
        {
            ownedClient?.Dispose();
        }
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "HttpClient owns and disposes the handler")]
    private static HttpClient CreateDefaultHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            CheckCertificateRevocationList = true
        };

        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    }
}
