using System.Linq;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

using NextIteration.SpectreConsole.SelfUpdate.Internal;

namespace NextIteration.SpectreConsole.SelfUpdate.Sources
{
    /// <summary>
    /// Generic <see cref="IUpdateSource"/> backed by a JSON manifest hosted
    /// at any HTTPS endpoint (web server, S3, Azure Blob, GitHub Pages,
    /// CDN — anything that serves static files). Useful for projects that
    /// don't want to wire updates to a specific hosting provider.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The manifest is a single JSON document of the shape:
    /// </para>
    /// <code>
    /// {
    ///   "tag": "v1.4.2",
    ///   "channel": "stable",                      // optional
    ///   "publishedAt": "2026-04-30T12:00:00Z",    // optional, ISO-8601
    ///   "releaseNotesUrl": "https://...",          // optional
    ///   "assets": [
    ///     {
    ///       "name": "myapp-v1.4.2-linux-x64.tar.gz",
    ///       "url":  "https://example.com/dl/myapp-v1.4.2-linux-x64.tar.gz",
    ///       "sizeBytes": 12345678,                 // optional
    ///       "contentType": "application/gzip",     // optional
    ///       "sha256": "abc123..."                  // optional, lowercase hex
    ///     }
    ///   ]
    /// }
    /// </code>
    /// <para>
    /// When an asset's <c>sha256</c> is populated, the default
    /// <see cref="Verification.Sha256ChecksumVerifier"/> picks it up via
    /// <see cref="ReleaseAsset.Metadata"/> with no further configuration —
    /// no <c>SHA256SUMS.txt</c> needed.
    /// </para>
    /// <para>
    /// Channel filtering: if the consumer requested a channel and the
    /// manifest's <c>channel</c> does not match, this source returns
    /// <see langword="null"/>. To support multiple channels, host one
    /// manifest per channel (e.g. <c>/releases/stable/latest.json</c>,
    /// <c>/releases/beta/latest.json</c>).
    /// </para>
    /// </remarks>
    public sealed class HttpManifestSource : IUpdateSource
    {
        internal const string Sha256MetadataKey = "sha256";

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly Uri _manifestUrl;

        /// <summary>Initializes a new instance.</summary>
        /// <param name="httpClientFactory">DI-resolved factory used per call.</param>
        /// <param name="manifestUrl">Absolute URL of the JSON manifest.</param>
        public HttpManifestSource(IHttpClientFactory httpClientFactory, Uri manifestUrl)
        {
            ArgumentNullException.ThrowIfNull(httpClientFactory);
            ArgumentNullException.ThrowIfNull(manifestUrl);

            _httpClientFactory = httpClientFactory;
            _manifestUrl = manifestUrl;
        }

        /// <inheritdoc />
        public async Task<RemoteRelease?> GetLatestAsync(string? channel, CancellationToken ct)
        {
            try
            {
                using var http = _httpClientFactory.CreateClient();
                using var resp = await http.GetAsync(_manifestUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;

                await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                var dto = await JsonSerializer.DeserializeAsync<ManifestDto>(stream, JsonOpts, ct).ConfigureAwait(false);
                if (dto is null || string.IsNullOrWhiteSpace(dto.Tag)) return null;

                if (channel is not null
                    && !string.IsNullOrWhiteSpace(dto.Channel)
                    && !string.Equals(channel, dto.Channel, StringComparison.OrdinalIgnoreCase))
                {
                    // Manifest is for a different channel — pretend we found nothing.
                    return null;
                }

                return Convert(dto, channel);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Source contract: swallow transient failures and return null.
                return null;
            }
        }

        /// <inheritdoc />
        public async Task DownloadAssetAsync(ReleaseAsset asset, Stream destination, IProgress<DownloadProgress>? progress, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(asset);
            ArgumentNullException.ThrowIfNull(destination);

            if (asset.DownloadUrl is null)
            {
                throw new UpdateException(
                    $"Cannot download asset '{asset.Name}': the manifest did not publish a download URL.");
            }

            using var http = _httpClientFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUrl);
            req.Headers.Accept.Clear();
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            var total = resp.Content.Headers.ContentLength ?? asset.SizeBytes;
            await using var content = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await StreamCopy.CopyWithProgressAsync(content, destination, total, progress, ct).ConfigureAwait(false);
        }

        private static RemoteRelease Convert(ManifestDto dto, string? channel)
        {
            var assets = (dto.Assets ?? Array.Empty<ManifestAssetDto>())
                .Where(a => !string.IsNullOrWhiteSpace(a.Name) && !string.IsNullOrWhiteSpace(a.Url))
                .Select(a => new ReleaseAsset(
                    Name: a.Name!,
                    DownloadUrl: new Uri(a.Url!, UriKind.Absolute),
                    SizeBytes: a.SizeBytes,
                    ContentType: a.ContentType,
                    Metadata: BuildMetadata(a)))
                .ToArray();

            Uri? notes = string.IsNullOrWhiteSpace(dto.ReleaseNotesUrl)
                ? null
                : new Uri(dto.ReleaseNotesUrl!, UriKind.Absolute);

            return new RemoteRelease(
                Tag: dto.Tag!,
                Channel: dto.Channel ?? channel,
                ReleaseNotesUrl: notes,
                Assets: assets,
                PublishedAt: dto.PublishedAt ?? DateTimeOffset.MinValue);
        }

        private static Dictionary<string, string> BuildMetadata(ManifestAssetDto a)
        {
            var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(a.Sha256))
            {
                meta[Sha256MetadataKey] = a.Sha256!.Trim();
            }
            return meta;
        }

        // ---------- DTOs ----------

        private sealed class ManifestDto
        {
            [JsonPropertyName("tag")] public string? Tag { get; init; }
            [JsonPropertyName("channel")] public string? Channel { get; init; }
            [JsonPropertyName("releaseNotesUrl")] public string? ReleaseNotesUrl { get; init; }
            [JsonPropertyName("publishedAt")] public DateTimeOffset? PublishedAt { get; init; }
            [JsonPropertyName("assets")] public ManifestAssetDto[]? Assets { get; init; }
        }

        private sealed class ManifestAssetDto
        {
            [JsonPropertyName("name")] public string? Name { get; init; }
            [JsonPropertyName("url")] public string? Url { get; init; }
            [JsonPropertyName("sizeBytes")] public long? SizeBytes { get; init; }
            [JsonPropertyName("contentType")] public string? ContentType { get; init; }
            [JsonPropertyName("sha256")] public string? Sha256 { get; init; }
        }
    }
}
