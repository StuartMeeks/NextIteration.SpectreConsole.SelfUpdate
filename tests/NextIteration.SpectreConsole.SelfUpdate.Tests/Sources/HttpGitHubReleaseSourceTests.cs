using NextIteration.SpectreConsole.SelfUpdate.Sources;
using NextIteration.SpectreConsole.SelfUpdate.Tests.Infrastructure;

using Xunit;

namespace NextIteration.SpectreConsole.SelfUpdate.Tests.Sources
{
    public sealed class HttpGitHubReleaseSourceTests
    {
        private static readonly Uri ApiBase = new("https://api.example.test/");

        [Fact]
        public async Task GetLatestAsync_with_no_channel_or_prereleases_hits_releases_latest_endpoint()
        {
            const string json = """
            {
              "tag_name": "v1.4.2",
              "html_url": "https://github.example/repo/releases/tag/v1.4.2",
              "draft": false,
              "prerelease": false,
              "published_at": "2026-04-30T12:00:00Z",
              "assets": [
                {
                  "name": "myapp-v1.4.2-linux-x64.tar.gz",
                  "url": "https://api.example.test/repos/acme/myapp/releases/assets/1",
                  "browser_download_url": "https://github.example/repo/releases/download/v1.4.2/myapp-v1.4.2-linux-x64.tar.gz",
                  "size": 12345,
                  "content_type": "application/gzip"
                }
              ]
            }
            """;
            HttpRequestMessage? captured = null;
            var handler = new FakeHttpHandler
            {
                Responder = req => { captured = req; return FakeHttpHandler.Json(json); },
            };
            var source = NewSource(handler, includePrereleases: false);

            var release = await source.GetLatestAsync(channel: null, CancellationToken.None);

            Assert.NotNull(release);
            Assert.Equal("v1.4.2", release!.Tag);
            Assert.NotNull(captured);
            Assert.EndsWith("/releases/latest", captured!.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GetLatestAsync_with_channel_lists_and_filters_by_tag_substring()
        {
            const string json = """
            [
              { "tag_name": "v1.5.0",         "draft": false, "prerelease": false, "published_at": "2026-04-30T12:00:00Z", "assets": [] },
              { "tag_name": "v1.5.1-beta.1",  "draft": false, "prerelease": true,  "published_at": "2026-05-01T12:00:00Z", "assets": [] },
              { "tag_name": "v1.5.0-alpha.1", "draft": false, "prerelease": true,  "published_at": "2026-04-15T12:00:00Z", "assets": [] }
            ]
            """;
            HttpRequestMessage? captured = null;
            var handler = new FakeHttpHandler
            {
                Responder = req => { captured = req; return FakeHttpHandler.Json(json); },
            };
            var source = NewSource(handler, includePrereleases: true);

            var release = await source.GetLatestAsync(channel: "beta", CancellationToken.None);

            Assert.NotNull(release);
            Assert.Equal("v1.5.1-beta.1", release!.Tag);
            Assert.Contains("/releases", captured?.RequestUri?.AbsolutePath ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("/latest", captured?.RequestUri?.AbsolutePath ?? string.Empty, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GetLatestAsync_with_includePrereleases_filters_drafts_only()
        {
            const string json = """
            [
              { "tag_name": "v2.0.0-rc.1",  "draft": false, "prerelease": true,  "published_at": "2026-05-02T12:00:00Z", "assets": [] },
              { "tag_name": "v1.0.0-draft", "draft": true,  "prerelease": false, "published_at": "2026-05-01T12:00:00Z", "assets": [] },
              { "tag_name": "v1.4.2",       "draft": false, "prerelease": false, "published_at": "2026-04-30T12:00:00Z", "assets": [] }
            ]
            """;
            var handler = new FakeHttpHandler { Responder = _ => FakeHttpHandler.Json(json) };
            var source = NewSource(handler, includePrereleases: true);

            // includePrereleases triggers the list path even with channel == null.
            var release = await source.GetLatestAsync(channel: null, CancellationToken.None);

            // Most recent non-draft is v2.0.0-rc.1.
            Assert.Equal("v2.0.0-rc.1", release?.Tag);
        }

        [Fact]
        public async Task GetLatestAsync_when_request_fails_returns_null()
        {
            var handler = new FakeHttpHandler { Responder = _ => FakeHttpHandler.NotFound() };
            var source = NewSource(handler, includePrereleases: false);

            var release = await source.GetLatestAsync(channel: null, CancellationToken.None);

            Assert.Null(release);
        }

        [Fact]
        public async Task GetLatestAsync_with_explicit_token_sets_authorization_header()
        {
            HttpRequestMessage? captured = null;
            var handler = new FakeHttpHandler
            {
                Responder = req =>
                {
                    captured = req;
                    return FakeHttpHandler.Json("""{ "tag_name": "v1.0.0", "assets": [] }""");
                },
            };
            var source = new HttpGitHubReleaseSource(
                new FakeHttpClientFactory(handler),
                "acme/myapp",
                token: "my-secret-token",
                includePrereleases: false,
                ApiBase);

            await source.GetLatestAsync(channel: null, CancellationToken.None);

            Assert.NotNull(captured);
            Assert.Equal("Bearer", captured!.Headers.Authorization?.Scheme);
            Assert.Equal("my-secret-token", captured.Headers.Authorization?.Parameter);
        }

        private static HttpGitHubReleaseSource NewSource(FakeHttpHandler handler, bool includePrereleases) =>
            new(new FakeHttpClientFactory(handler),
                "acme/myapp",
                token: null,
                includePrereleases,
                ApiBase);
    }
}
