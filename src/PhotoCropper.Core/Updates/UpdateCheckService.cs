using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Reflection;

namespace PhotoCropper.Core.Updates;

public static class UpdateCheckService
{
    private static readonly Uri DefaultReleaseUri = new("https://github.com/Atm0n/PhotoCropper/releases/latest");

    public static Uri DefaultLatestReleaseUri => DefaultReleaseUri;

    public static Version GetCurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(UpdateCheckService).Assembly;
        var ver = assembly.GetName().Version;
        return ver != null ? new Version(ver.Major, ver.Minor, Math.Max(0, ver.Build)) : new Version(2, 4, 0);
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
            using var request = new HttpRequestMessage(HttpMethod.Head, requestUri);
            request.Headers.Add("User-Agent", "PhotoCropper");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            Uri? location = response.Headers.Location;
            if (location == null && response.RequestMessage?.RequestUri != null)
            {
                location = response.RequestMessage.RequestUri;
            }

            if (location == null)
            {
                return UpdateCheckResult.UpToDate(activeVersion);
            }

            Uri absoluteUri = location.IsAbsoluteUri ? location : new Uri(requestUri, location);
            string tag = Path.GetFileName(absoluteUri.AbsolutePath.TrimEnd('/'));

            if (TryParseTagVersion(tag, out Version? latestVersion) && latestVersion != null)
            {
                if (latestVersion > activeVersion)
                {
                    return UpdateCheckResult.Available(activeVersion, latestVersion, tag, absoluteUri);
                }

                return UpdateCheckResult.UpToDate(activeVersion, latestVersion, tag, absoluteUri);
            }

            return UpdateCheckResult.UpToDate(activeVersion);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
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
            AllowAutoRedirect = false,
            CheckCertificateRevocationList = true
        };

        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    }
}
