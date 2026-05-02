using System.Linq;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

using NextIteration.SpectreConsole.SelfUpdate.Internal;

namespace NextIteration.SpectreConsole.SelfUpdate.Sources
{
    /// <summary>
    /// Default <see cref="IUpdateSource"/>. Talks to the GitHub Releases REST
    /// API directly using <see cref="HttpClient"/>. Works for both public and
    /// private repositories (the latter requires a token via
    /// <see cref="SelfUpdaterOptions.GitHubToken"/> or the <c>GITHUB_TOKEN</c>
    /// / <c>GH_TOKEN</c> environment variables).
    /// </summary>
    /// <remarks>
    /// Channel semantics: <see langword="null"/> with
    /// <see cref="SelfUpdaterOptions.IncludePrereleases"/> = false hits
    /// <c>/releases/latest</c>, which excludes drafts and prereleases. Any
    /// other combination lists <c>/releases</c> (newest first) and returns
    /// the first entry whose tag contains <c>-{channel}</c> (case-insensitive)
    /// when a channel is specified, or the first non-draft entry when only
    /// prereleases are enabled. Asset downloads always use the API asset URL
    /// with <c>Accept: application/octet-stream</c> so private repos work
    /// out of the box.
    /// </remarks>
    public sealed class HttpGitHubReleaseSource : IUpdateSource
    {
        internal const string DefaultApiBase = "https://api.github.com";
        internal const string UserAgent = "NextIteration.SpectreConsole.SelfUpdate";
        private const string GitHubJsonMediaType = "application/vnd.github+json";
        private const string OctetStreamMediaType = "application/octet-stream";

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _repository;
        private readonly string? _explicitToken;
        private readonly bool _includePrereleases;
        private readonly Uri _apiBase;

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="httpClientFactory">DI-resolved factory used to obtain a fresh client per call.</param>
        /// <param name="repository">The <c>owner/repo</c> slug (e.g. <c>"acme/my-cli"</c>).</param>
        /// <param name="token">
        /// Optional bearer token. When <see langword="null"/> the source falls
        /// back to <c>GITHUB_TOKEN</c> then <c>GH_TOKEN</c> environment
        /// variables; if neither is set, requests are anonymous (works for
        /// public repos within the unauthenticated rate limit).
        /// </param>
        /// <param name="includePrereleases">When <see langword="true"/>, prerelease tags are eligible to be returned.</param>
        public HttpGitHubReleaseSource(
            IHttpClientFactory httpClientFactory,
            string repository,
            string? token,
            bool includePrereleases)
            : this(httpClientFactory, repository, token, includePrereleases, new Uri(DefaultApiBase))
        {
        }

        internal HttpGitHubReleaseSource(
            IHttpClientFactory httpClientFactory,
            string repository,
            string? token,
            bool includePrereleases,
            Uri apiBase)
        {
            ArgumentNullException.ThrowIfNull(httpClientFactory);
            ArgumentException.ThrowIfNullOrWhiteSpace(repository);
            ArgumentNullException.ThrowIfNull(apiBase);

            _httpClientFactory = httpClientFactory;
            _repository = repository;
            _explicitToken = token;
            _includePrereleases = includePrereleases;
            _apiBase = apiBase;
        }

        /// <inheritdoc />
        public async Task<RemoteRelease?> GetLatestAsync(string? channel, CancellationToken ct)
        {
            try
            {
                using var http = _httpClientFactory.CreateClient();
                ConfigureRequestHeaders(http);

                if (channel is null && !_includePrereleases)
                {
                    var dto = await GetJsonAsync<GitHubReleaseDto>(http, $"repos/{_repository}/releases/latest", ct).ConfigureAwait(false);
                    return dto is null ? null : Convert(dto, channel);
                }

                var releases = await GetJsonAsync<GitHubReleaseDto[]>(http, $"repos/{_repository}/releases?per_page=30", ct).ConfigureAwait(false);
                if (releases is null || releases.Length == 0) return null;

                var match = releases
                    .Where(r => !r.Draft)
                    .Where(r => _includePrereleases || !r.Prerelease)
                    .Where(r => channel is null
                        || (r.TagName ?? string.Empty).Contains($"-{channel}", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(r => r.PublishedAt)
                    .FirstOrDefault();

                return match is null ? null : Convert(match, channel);
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
                    $"Cannot download asset '{asset.Name}': no download URL was published for it.");
            }

            using var http = _httpClientFactory.CreateClient();
            ConfigureRequestHeaders(http);

            // Asset.DownloadUrl points at the API asset URL — fetching with
            // Accept: application/octet-stream redirects to a signed CDN URL.
            using var req = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUrl);
            req.Headers.Accept.Clear();
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(OctetStreamMediaType));

            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            var total = resp.Content.Headers.ContentLength ?? asset.SizeBytes;
            await using var content = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await StreamCopy.CopyWithProgressAsync(content, destination, total, progress, ct).ConfigureAwait(false);
        }

        private void ConfigureRequestHeaders(HttpClient http)
        {
            http.DefaultRequestHeaders.UserAgent.Clear();
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(UserAgent, PackageVersion.Current));
            http.DefaultRequestHeaders.Accept.Clear();
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(GitHubJsonMediaType));
            http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

            var token = ResolveToken();
            if (!string.IsNullOrWhiteSpace(token))
            {
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }

        private string? ResolveToken()
        {
            if (!string.IsNullOrWhiteSpace(_explicitToken)) return _explicitToken;
            return Environment.GetEnvironmentVariable("GITHUB_TOKEN")
                ?? Environment.GetEnvironmentVariable("GH_TOKEN");
        }

        private async Task<T?> GetJsonAsync<T>(HttpClient http, string relativePath, CancellationToken ct)
        {
            var url = new Uri(_apiBase, relativePath);
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return default;
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOpts, ct).ConfigureAwait(false);
        }

        private static RemoteRelease? Convert(GitHubReleaseDto dto, string? channel)
        {
            if (string.IsNullOrWhiteSpace(dto.TagName)) return null;

            var assets = (dto.Assets ?? Array.Empty<GitHubAssetDto>())
                .Where(a => !string.IsNullOrWhiteSpace(a.Name) && !string.IsNullOrWhiteSpace(a.Url))
                .Select(a => new ReleaseAsset(
                    Name: a.Name!,
                    DownloadUrl: new Uri(a.Url!),
                    SizeBytes: a.Size,
                    ContentType: a.ContentType,
                    Metadata: BuildAssetMetadata(a)))
                .ToArray();

            Uri? notes = string.IsNullOrWhiteSpace(dto.HtmlUrl) ? null : new Uri(dto.HtmlUrl);

            // Sources should report channel based on what they observed —
            // forward the caller's channel filter as the resolved channel
            // when the matched release does not declare one explicitly.
            var resolvedChannel = dto.Prerelease ? (channel ?? "prerelease") : channel;

            return new RemoteRelease(
                Tag: dto.TagName!,
                Channel: resolvedChannel,
                ReleaseNotesUrl: notes,
                Assets: assets,
                PublishedAt: dto.PublishedAt ?? DateTimeOffset.MinValue);
        }

        private static Dictionary<string, string> BuildAssetMetadata(GitHubAssetDto a)
        {
            var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(a.BrowserDownloadUrl))
            {
                meta["browserDownloadUrl"] = a.BrowserDownloadUrl!;
            }
            if (!string.IsNullOrWhiteSpace(a.Url))
            {
                meta["apiUrl"] = a.Url!;
            }
            return meta;
        }

        // ---------- DTOs ----------

        private sealed class GitHubReleaseDto
        {
            [JsonPropertyName("tag_name")] public string? TagName { get; init; }
            [JsonPropertyName("html_url")] public string? HtmlUrl { get; init; }
            [JsonPropertyName("draft")] public bool Draft { get; init; }
            [JsonPropertyName("prerelease")] public bool Prerelease { get; init; }
            [JsonPropertyName("published_at")] public DateTimeOffset? PublishedAt { get; init; }
            [JsonPropertyName("assets")] public GitHubAssetDto[]? Assets { get; init; }
        }

        private sealed class GitHubAssetDto
        {
            [JsonPropertyName("name")] public string? Name { get; init; }
            [JsonPropertyName("url")] public string? Url { get; init; }
            [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; init; }
            [JsonPropertyName("size")] public long? Size { get; init; }
            [JsonPropertyName("content_type")] public string? ContentType { get; init; }
        }
    }
}
