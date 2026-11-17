using PhotoCropper.Core.Updates;
using System.Net;

namespace PhotoCropper.Core.Tests.Updates;

public sealed class UpdateCheckServiceTests
{
    [Theory]
    [InlineData("v2.4.0", 2, 4, 0)]
    [InlineData("V2.5.1", 2, 5, 1)]
    [InlineData("3.0.0", 3, 0, 0)]
    [InlineData("v1.2", 1, 2, 0)]
    [InlineData("v2.5.0-preview1", 2, 5, 0)]
    public void TryParseTagVersion_ValidTags_ShouldParseCorrectly(string tag, int expectedMajor, int expectedMinor, int expectedBuild)
    {
        bool success = UpdateCheckService.TryParseTagVersion(tag, out Version? version);

        success.ShouldBeTrue();
        version.ShouldNotBeNull();
        version.Major.ShouldBe(expectedMajor);
        version.Minor.ShouldBe(expectedMinor);
        Math.Max(0, version.Build).ShouldBe(expectedBuild);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid-tag")]
    [InlineData("v-beta")]
    public void TryParseTagVersion_InvalidTags_ShouldReturnFalse(string? tag)
    {
        bool success = UpdateCheckService.TryParseTagVersion(tag, out Version? version);

        success.ShouldBeFalse();
        version.ShouldBeNull();
    }

    [Fact]
    public void GetCurrentVersion_ShouldReturnNonNullVersion()
    {
        var version = UpdateCheckService.GetCurrentVersion();

        version.ShouldNotBeNull();
        version.Major.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task CheckForUpdateAsync_NewerVersionFound_ShouldReturnAvailable()
    {
        var redirectUri = new Uri("https://github.com/Atm0n/PhotoCropper/releases/tag/v9.9.9");
        using var handler = new MockRedirectHandler(HttpStatusCode.Redirect, redirectUri);
        using var client = new HttpClient(handler);

        var currentVersion = new Version(2, 4, 0);
        var result = await UpdateCheckService.CheckForUpdateAsync(
            currentVersion,
            new Uri("https://github.com/Atm0n/PhotoCropper/releases/latest"),
            client,
            TestContext.Current.CancellationToken);

        result.IsUpdateAvailable.ShouldBeTrue();
        result.CurrentVersion.ShouldBe(currentVersion);
        result.LatestVersion.ShouldBe(new Version(9, 9, 9));
        result.Tag.ShouldBe("v9.9.9");
        result.ReleaseUri.ShouldBe(redirectUri);
        result.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public async Task CheckForUpdateAsync_SameVersionFound_ShouldReturnUpToDate()
    {
        var redirectUri = new Uri("https://github.com/Atm0n/PhotoCropper/releases/tag/v2.4.0");
        using var handler = new MockRedirectHandler(HttpStatusCode.Redirect, redirectUri);
        using var client = new HttpClient(handler);

        var currentVersion = new Version(2, 4, 0);
        var result = await UpdateCheckService.CheckForUpdateAsync(
            currentVersion,
            new Uri("https://github.com/Atm0n/PhotoCropper/releases/latest"),
            client,
            TestContext.Current.CancellationToken);

        result.IsUpdateAvailable.ShouldBeFalse();
        result.CurrentVersion.ShouldBe(currentVersion);
        result.LatestVersion.ShouldBe(new Version(2, 4, 0));
        result.Tag.ShouldBe("v2.4.0");
        result.ReleaseUri.ShouldBe(redirectUri);
    }

    [Fact]
    public async Task CheckForUpdateAsync_OlderVersionFound_ShouldReturnUpToDate()
    {
        var redirectUri = new Uri("https://github.com/Atm0n/PhotoCropper/releases/tag/v2.3.0");
        using var handler = new MockRedirectHandler(HttpStatusCode.Redirect, redirectUri);
        using var client = new HttpClient(handler);

        var currentVersion = new Version(2, 4, 0);
        var result = await UpdateCheckService.CheckForUpdateAsync(
            currentVersion,
            new Uri("https://github.com/Atm0n/PhotoCropper/releases/latest"),
            client,
            TestContext.Current.CancellationToken);

        result.IsUpdateAvailable.ShouldBeFalse();
        result.CurrentVersion.ShouldBe(currentVersion);
    }

    [Fact]
    public async Task CheckForUpdateAsync_GitHubApiSuccess_ShouldReturnAvailableWithHtmlUrl()
    {
        string json = """
        {
          "tag_name": "v9.9.9",
          "name": "PhotoCropper v9.9.9",
          "html_url": "https://github.com/Atm0n/PhotoCropper/releases/tag/v9.9.9"
        }
        """;
        using var handler = new MockJsonResponseHandler(HttpStatusCode.OK, json);
        using var client = new HttpClient(handler);

        var currentVersion = new Version(2, 4, 0);
        var result = await UpdateCheckService.CheckForUpdateAsync(
            currentVersion,
            new Uri("https://api.github.com/repos/Atm0n/PhotoCropper/releases/latest"),
            client,
            TestContext.Current.CancellationToken);

        result.IsUpdateAvailable.ShouldBeTrue();
        result.CurrentVersion.ShouldBe(currentVersion);
        result.LatestVersion.ShouldBe(new Version(9, 9, 9));
        result.Tag.ShouldBe("v9.9.9");
        result.ReleaseUri.ShouldBe(new Uri("https://github.com/Atm0n/PhotoCropper/releases/tag/v9.9.9"));
        result.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public async Task CheckForUpdateAsync_NetworkError_ShouldReturnFailed()
    {
        using var handler = new ThrowingHandler(new HttpRequestException("Connection refused"));
        using var client = new HttpClient(handler);

        var currentVersion = new Version(2, 4, 0);
        var result = await UpdateCheckService.CheckForUpdateAsync(
            currentVersion,
            new Uri("https://api.github.com/repos/Atm0n/PhotoCropper/releases/latest"),
            client,
            TestContext.Current.CancellationToken);

        result.IsUpdateAvailable.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNull();
        result.ErrorMessage.ShouldContain("Connection refused");
    }

    private sealed class MockJsonResponseHandler(HttpStatusCode statusCode, string jsonContent) : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode = statusCode;
        private readonly string _jsonContent = jsonContent;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_jsonContent, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class MockRedirectHandler(HttpStatusCode statusCode, Uri location) : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode = statusCode;
        private readonly Uri _location = location;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode);
            response.Headers.Location = _location;
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task CheckForUpdateAsync_RateLimited_ShouldReturnFailedNotUpToDate()
    {
        using var handler = new StatusHandler(HttpStatusCode.Forbidden, rateLimitRemaining: "0");
        using var client = new HttpClient(handler);

        var result = await UpdateCheckService.CheckForUpdateAsync(
            new Version(2, 4, 0),
            new Uri("https://api.github.com/repos/Atm0n/PhotoCropper/releases/latest"),
            client,
            TestContext.Current.CancellationToken);

        result.IsUpdateAvailable.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNull();
        result.ErrorMessage.ShouldContain("rate limit");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task CheckForUpdateAsync_HttpError_ShouldReturnFailedWithStatus(HttpStatusCode status)
    {
        using var handler = new StatusHandler(status);
        using var client = new HttpClient(handler);

        var result = await UpdateCheckService.CheckForUpdateAsync(
            new Version(2, 4, 0),
            new Uri("https://api.github.com/repos/Atm0n/PhotoCropper/releases/latest"),
            client,
            TestContext.Current.CancellationToken);

        result.ErrorMessage.ShouldNotBeNull();
        result.ErrorMessage.ShouldContain(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task CheckForUpdateAsync_SuccessWithoutTag_ShouldReturnFailed()
    {
        using var handler = new MockJsonResponseHandler(HttpStatusCode.OK, """{ "name": "no tag here" }""");
        using var client = new HttpClient(handler);

        var result = await UpdateCheckService.CheckForUpdateAsync(
            new Version(2, 4, 0),
            new Uri("https://api.github.com/repos/Atm0n/PhotoCropper/releases/latest"),
            client,
            TestContext.Current.CancellationToken);

        result.ErrorMessage.ShouldNotBeNull();
    }

    private sealed class StatusHandler(HttpStatusCode statusCode, string? rateLimitRemaining = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode);
            if (rateLimitRemaining != null)
            {
                response.Headers.Add("X-RateLimit-Remaining", rateLimitRemaining);
            }
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        private readonly Exception _exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromException<HttpResponseMessage>(_exception);
        }
    }
}
