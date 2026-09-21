using PhotoCropper.Core.Updates;
using Shouldly;
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
    public async Task CheckForUpdateAsync_NetworkError_ShouldReturnFailed()
    {
        using var handler = new ThrowingHandler(new HttpRequestException("Connection refused"));
        using var client = new HttpClient(handler);

        var currentVersion = new Version(2, 4, 0);
        var result = await UpdateCheckService.CheckForUpdateAsync(
            currentVersion,
            new Uri("https://github.com/Atm0n/PhotoCropper/releases/latest"),
            client,
            TestContext.Current.CancellationToken);

        result.IsUpdateAvailable.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNull();
        result.ErrorMessage.ShouldContain("Connection refused");
    }

    private sealed class MockRedirectHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly Uri _location;

        public MockRedirectHandler(HttpStatusCode statusCode, Uri location)
        {
            _statusCode = statusCode;
            _location = location;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode);
            response.Headers.Location = _location;
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _exception;

        public ThrowingHandler(Exception exception)
        {
            _exception = exception;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromException<HttpResponseMessage>(_exception);
        }
    }
}
