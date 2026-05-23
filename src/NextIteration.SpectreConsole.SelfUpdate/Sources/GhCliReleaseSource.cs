using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

using NextIteration.SpectreConsole.SelfUpdate.Internal;

namespace NextIteration.SpectreConsole.SelfUpdate.Sources
{
    /// <summary>
    /// <see cref="IUpdateSource"/> for private GitHub repositories that
    /// shells out to the GitHub <c>gh</c> CLI. Reuses whatever <c>gh
    /// auth</c> session the caller already has, so consumers don't need to
    /// thread tokens through configuration. Requires <c>gh</c> on
    /// <c>PATH</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Channel semantics mirror <c>HttpGitHubReleaseSource</c>: a
    /// <see langword="null"/> channel without prereleases hits
    /// <c>gh release view</c> (the latest non-draft non-prerelease);
    /// otherwise the source lists the most recent 30 releases via
    /// <c>gh release list</c> and applies the same draft / prerelease /
    /// channel-tag filters in-process.
    /// </para>
    /// <para>
    /// Asset downloads use <c>gh release download &lt;tag&gt; --repo &lt;slug&gt;
    /// --pattern &lt;name&gt; --output &lt;tempfile&gt;</c> followed by streaming
    /// the temp file into the caller's destination. The tag is recovered
    /// from <see cref="ReleaseAsset.Metadata"/> (key <c>"tag"</c>) which
    /// the source populates during <see cref="GetLatestAsync(string?, CancellationToken)"/>. Arguments
    /// are passed via <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/>
    /// so values from a remote source never need to be quoted or escaped.
    /// </para>
    /// </remarks>
    public sealed class GhCliReleaseSource : IUpdateSource
    {
        internal const string TagMetadataKey = "tag";
        private static readonly TimeSpan DefaultViewTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan DefaultListTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan DefaultDownloadTimeout = TimeSpan.FromMinutes(5);

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        private readonly string _repository;
        private readonly bool _includePrereleases;
        private readonly Func<IReadOnlyList<string>, TimeSpan, CancellationToken, Task<string>> _runner;

        /// <summary>Initializes a new instance backed by the real <c>gh</c> binary.</summary>
        /// <param name="repository">The <c>owner/repo</c> slug.</param>
        /// <param name="includePrereleases">When <see langword="true"/>, prerelease tags are eligible.</param>
        public GhCliReleaseSource(string repository, bool includePrereleases)
            : this(repository, includePrereleases, GhProcess.RunCaptureStdoutAsync)
        {
        }

        /// <summary>
        /// Test seam: initialize a new instance with a custom process
        /// runner. Production code uses the parameterless overload.
        /// </summary>
        internal GhCliReleaseSource(
            string repository,
            bool includePrereleases,
            Func<IReadOnlyList<string>, TimeSpan, CancellationToken, Task<string>> runner)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(repository);
            ArgumentNullException.ThrowIfNull(runner);

            _repository = repository;
            _includePrereleases = includePrereleases;
            _runner = runner;
        }

        /// <inheritdoc />
        public Task<RemoteRelease?> GetLatestAsync(string? channel, CancellationToken ct) =>
            GetLatestAsync(channel, includePrereleasesOverride: null, ct);

        /// <inheritdoc />
        public async Task<RemoteRelease?> GetLatestAsync(string? channel, bool? includePrereleasesOverride, CancellationToken ct)
        {
            var includePrereleases = includePrereleasesOverride ?? _includePrereleases;
            try
            {
                if (channel is null && !includePrereleases)
                {
                    var stdout = await _runner(
                        new[]
                        {
                            "release", "view",
                            "--json", "tagName,name,url,publishedAt,isDraft,isPrerelease,assets",
                            "--repo", _repository,
                        },
                        DefaultViewTimeout,
                        ct).ConfigureAwait(false);
                    var dto = JsonSerializer.Deserialize<GhReleaseDto>(stdout, JsonOpts);
                    return dto is null ? null : Convert(dto, channel);
                }

                var listJson = await _runner(
                    new[]
                    {
                        "release", "list",
                        "--json", "tagName,name,url,publishedAt,isDraft,isPrerelease",
                        "--limit", "30",
                        "--repo", _repository,
                    },
                    DefaultListTimeout,
                    ct).ConfigureAwait(false);
                var releases = JsonSerializer.Deserialize<GhReleaseDto[]>(listJson, JsonOpts) ?? Array.Empty<GhReleaseDto>();
                var match = releases
                    .Where(r => !r.IsDraft)
                    .Where(r => includePrereleases || !r.IsPrerelease)
                    .Where(r => channel is null
                        || (r.TagName ?? string.Empty).Contains($"-{channel}", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(r => r.PublishedAt)
                    .FirstOrDefault();
                if (match is null) return null;

                // `release list` doesn't return assets — fetch the matched
                // release in detail so DownloadAssetAsync has something to act on.
                var detailJson = await _runner(
                    new[]
                    {
                        "release", "view", match.TagName!,
                        "--json", "tagName,name,url,publishedAt,isDraft,isPrerelease,assets",
                        "--repo", _repository,
                    },
                    DefaultViewTimeout,
                    ct).ConfigureAwait(false);
                var detail = JsonSerializer.Deserialize<GhReleaseDto>(detailJson, JsonOpts);
                return detail is null ? null : Convert(detail, channel);
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

            if (!asset.Metadata.TryGetValue(TagMetadataKey, out var tag) || string.IsNullOrWhiteSpace(tag))
            {
                throw new UpdateException(
                    $"GhCliReleaseSource cannot download '{asset.Name}': release tag is not present in asset metadata.");
            }

            var tempFile = Path.Combine(Path.GetTempPath(), $"selfupdate-gh-{Guid.NewGuid():N}-{asset.Name}");
            try
            {
                progress?.Report(new DownloadProgress(0, asset.SizeBytes));

                await _runner(
                    new[]
                    {
                        "release", "download", tag,
                        "--repo", _repository,
                        "--pattern", asset.Name,
                        "--output", tempFile,
                        "--clobber",
                    },
                    DefaultDownloadTimeout,
                    ct).ConfigureAwait(false);

                await using var src = File.OpenRead(tempFile);
                await StreamCopy.CopyWithProgressAsync(src, destination, asset.SizeBytes, progress, ct).ConfigureAwait(false);
            }
            catch (GhProcessException ex)
            {
                throw new UpdateException($"gh release download failed for '{asset.Name}': {ex.Message}", ex);
            }
            finally
            {
                TryDelete(tempFile);
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch
            {
                // Best effort.
            }
        }

        private static RemoteRelease? Convert(GhReleaseDto dto, string? channel)
        {
            if (string.IsNullOrWhiteSpace(dto.TagName)) return null;

            var tag = dto.TagName!;
            var assets = (dto.Assets ?? Array.Empty<GhAssetDto>())
                .Where(a => !string.IsNullOrWhiteSpace(a.Name))
                .Select(a => new ReleaseAsset(
                    Name: a.Name!,
                    DownloadUrl: TryParseUri(a.Url),
                    SizeBytes: a.Size,
                    ContentType: a.ContentType,
                    Metadata: BuildAssetMetadata(tag)))
                .ToArray();

            Uri? notes = TryParseUri(dto.Url);

            var resolvedChannel = dto.IsPrerelease ? (channel ?? "prerelease") : channel;

            return new RemoteRelease(
                Tag: tag,
                Channel: resolvedChannel,
                ReleaseNotesUrl: notes,
                Assets: assets,
                PublishedAt: dto.PublishedAt ?? DateTimeOffset.MinValue);
        }

        private static Dictionary<string, string> BuildAssetMetadata(string tag) => new(StringComparer.OrdinalIgnoreCase)
        {
            [TagMetadataKey] = tag,
        };

        private static Uri? TryParseUri(string? value) =>
            !string.IsNullOrWhiteSpace(value) && Uri.TryCreate(value, UriKind.Absolute, out var uri)
                ? uri
                : null;

        // ---------- DTOs ----------

        private sealed class GhReleaseDto
        {
            [JsonPropertyName("tagName")] public string? TagName { get; init; }
            [JsonPropertyName("name")] public string? Name { get; init; }
            [JsonPropertyName("url")] public string? Url { get; init; }
            [JsonPropertyName("publishedAt")] public DateTimeOffset? PublishedAt { get; init; }
            [JsonPropertyName("isDraft")] public bool IsDraft { get; init; }
            [JsonPropertyName("isPrerelease")] public bool IsPrerelease { get; init; }
            [JsonPropertyName("assets")] public GhAssetDto[]? Assets { get; init; }
        }

        private sealed class GhAssetDto
        {
            [JsonPropertyName("name")] public string? Name { get; init; }
            [JsonPropertyName("url")] public string? Url { get; init; }
            [JsonPropertyName("size")] public long? Size { get; init; }
            [JsonPropertyName("contentType")] public string? ContentType { get; init; }
        }
    }
}
