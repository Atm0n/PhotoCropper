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
        if (trimmed is ['v' or 'V', ..])
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

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

            string? tag = null;
            Uri? downloadUri = null;

            if (response.IsSuccessStatusCode)
            {
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
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
                // A redirect the client didn't follow (e.g. a caller-supplied client hitting the
                // github.com/.../releases/latest page): the target URL ends in the release tag.
                Uri loc = response.Headers.Location;
                downloadUri = loc.IsAbsoluteUri ? loc : new Uri(requestUri, loc);
                tag = Path.GetFileName(downloadUri.AbsolutePath.TrimEnd('/'));
            }
            else
            {
                // Any other error (e.g. 403/429 when the unauthenticated GitHub API rate limit is hit) means
                // the latest version is unknown. Reporting "up to date" here would hide available updates.
                return UpdateCheckResult.Failed(activeVersion, DescribeHttpFailure(response));
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

            return UpdateCheckResult.Failed(activeVersion, $"Could not read the latest release version (tag: '{tag ?? "none"}').");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return UpdateCheckResult.Failed(activeVersion, ex.Message);
        }
        finally
        {
            ownedClient?.Dispose();
        }
    }

    private static string DescribeHttpFailure(HttpResponseMessage response)
    {
        int status = (int)response.StatusCode;
        string message = $"GitHub returned HTTP {status} {response.ReasonPhrase}".TrimEnd();

        bool rateLimited = status == 429 ||
            (status == 403 && response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.Contains("0"));
        return rateLimited
            ? $"{message}: the GitHub API rate limit was reached. Try again later."
            : message;
    }

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
