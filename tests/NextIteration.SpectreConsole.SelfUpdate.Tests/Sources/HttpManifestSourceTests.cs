using NextIteration.SpectreConsole.SelfUpdate.Sources;
using NextIteration.SpectreConsole.SelfUpdate.Tests.Infrastructure;

using Xunit;

namespace NextIteration.SpectreConsole.SelfUpdate.Tests.Sources
{
    public sealed class HttpManifestSourceTests
    {
        private static readonly Uri ManifestUrl = new("https://example.com/latest.json");

        [Fact]
        public async Task GetLatestAsync_parses_manifest_and_returns_release()
        {
            var json = """
            {
              "tag": "v1.4.2",
              "channel": "stable",
              "publishedAt": "2026-04-30T12:00:00+00:00",
              "releaseNotesUrl": "https://example.com/notes",
              "assets": [
                {
                  "name": "myapp-v1.4.2-linux-x64.tar.gz",
                  "url": "https://example.com/dl/myapp-v1.4.2-linux-x64.tar.gz",
                  "sizeBytes": 12345,
                  "contentType": "application/gzip",
                  "sha256": "abc123def4567890abc123def4567890abc123def4567890abc123def4567890"
                }
              ]
            }
            """;
            var handler = new FakeHttpHandler { Responder = _ => FakeHttpHandler.Json(json) };
            var source = new HttpManifestSource(new FakeHttpClientFactory(handler), ManifestUrl);

            var release = await source.GetLatestAsync(channel: null, CancellationToken.None);

            Assert.NotNull(release);
            Assert.Equal("v1.4.2", release!.Tag);
            Assert.Equal("stable", release.Channel);
            Assert.Single(release.Assets);
            Assert.Equal("abc123def4567890abc123def4567890abc123def4567890abc123def4567890",
                release.Assets[0].Metadata["sha256"]);
        }

        [Fact]
        public async Task GetLatestAsync_when_channel_mismatches_returns_null()
        {
            var json = """{ "tag": "v1.0.0", "channel": "beta", "assets": [] }""";
            var handler = new FakeHttpHandler { Responder = _ => FakeHttpHandler.Json(json) };
            var source = new HttpManifestSource(new FakeHttpClientFactory(handler), ManifestUrl);

            var release = await source.GetLatestAsync(channel: "stable", CancellationToken.None);

            Assert.Null(release);
        }

        [Fact]
        public async Task GetLatestAsync_when_request_fails_returns_null()
        {
            var handler = new FakeHttpHandler { Responder = _ => FakeHttpHandler.NotFound() };
            var source = new HttpManifestSource(new FakeHttpClientFactory(handler), ManifestUrl);

            var release = await source.GetLatestAsync(channel: null, CancellationToken.None);

            Assert.Null(release);
        }

        [Fact]
        public async Task DownloadAssetAsync_streams_bytes_to_destination()
        {
            var payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0xFF };
            var assetUrl = new Uri("https://example.com/dl/asset.tar.gz");
            var handler = new FakeHttpHandler
            {
                Responder = req => req.RequestUri == assetUrl
                    ? FakeHttpHandler.Bytes(payload)
                    : throw new InvalidOperationException("unexpected request URI"),
            };
            var source = new HttpManifestSource(new FakeHttpClientFactory(handler), ManifestUrl);

            using var ms = new MemoryStream();
            var asset = new ReleaseAsset("asset.tar.gz", assetUrl, payload.LongLength, "application/gzip", new Dictionary<string, string>());

            await source.DownloadAssetAsync(asset, ms, progress: null, CancellationToken.None);

            Assert.Equal(payload, ms.ToArray());
        }
    }
}
