using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NextIteration.SpectreConsole.SelfUpdate.Verification
{
    /// <summary>
    /// Default <see cref="IPackageVerifier"/>. Confirms the downloaded
    /// archive matches a published SHA-256 hash. Two sources of truth, in
    /// order:
    /// <list type="number">
    ///   <item><description>
    ///     <c>ReleaseAsset.Metadata["sha256"]</c> when the
    ///     <see cref="IUpdateSource"/> populated it (the
    ///     <c>HttpManifestSource</c> does this automatically when the
    ///     manifest publishes <c>sha256</c> per asset).
    ///   </description></item>
    ///   <item><description>
    ///     A <c>SHA256SUMS.txt</c> sibling asset on the same release —
    ///     downloaded via the registered <see cref="IUpdateSource"/> and
    ///     parsed by <see cref="Sha256SumsManifest"/>.
    ///   </description></item>
    /// </list>
    /// Throws <see cref="UpdateException"/> when no hash is available or
    /// when the computed hash mismatches the expected one.
    /// </summary>
    public sealed class Sha256ChecksumVerifier : IPackageVerifier
    {
        private static readonly string[] ManifestNames =
        {
            "SHA256SUMS.txt",
            "SHA256SUMS",
            "sha256sums.txt",
            "sha256sums",
            "checksums.txt",
        };

        private readonly IUpdateSource _source;

        /// <summary>
        /// Initializes a new instance backed by the supplied
        /// <paramref name="source"/>. The source is used when the asset's
        /// metadata does not include a <c>sha256</c> entry — the verifier
        /// will look for and download a sibling <c>SHA256SUMS.txt</c> in
        /// the release.
        /// </summary>
        public Sha256ChecksumVerifier(IUpdateSource source)
        {
            ArgumentNullException.ThrowIfNull(source);
            _source = source;
        }

        /// <inheritdoc />
        public async Task VerifyAsync(string downloadedFilePath, RemoteRelease release, ReleaseAsset asset, CancellationToken ct)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(downloadedFilePath);
            ArgumentNullException.ThrowIfNull(release);
            ArgumentNullException.ThrowIfNull(asset);

            var expected = TryReadMetadataSha256(asset)
                ?? await TryFetchManifestSha256Async(release, asset, ct).ConfigureAwait(false);

            if (expected is null)
            {
                throw new UpdateException(
                    $"SHA-256 hash for '{asset.Name}' is not available. The asset's metadata does not include a 'sha256' entry, and no SHA256SUMS.txt asset was found on release '{release.Tag}'.");
            }

            var actual = await ComputeSha256Async(downloadedFilePath, ct).ConfigureAwait(false);
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateException(
                    $"SHA-256 mismatch for '{asset.Name}'. Expected '{expected}', got '{actual}'.");
            }
        }

        private static string? TryReadMetadataSha256(ReleaseAsset asset)
        {
            if (asset.Metadata.TryGetValue("sha256", out var sha)
                && !string.IsNullOrWhiteSpace(sha)
                && sha.Length == 64)
            {
                return sha.Trim().ToLowerInvariant();
            }
            return null;
        }

        private async Task<string?> TryFetchManifestSha256Async(RemoteRelease release, ReleaseAsset asset, CancellationToken ct)
        {
            var manifest = release.Assets.FirstOrDefault(a =>
                ManifestNames.Contains(a.Name, StringComparer.OrdinalIgnoreCase));
            if (manifest is null) return null;

            using var ms = new MemoryStream();
            await _source.DownloadAssetAsync(manifest, ms, progress: null, ct).ConfigureAwait(false);

            var content = Encoding.UTF8.GetString(ms.ToArray());
            var lookup = Sha256SumsManifest.Parse(content);
            return lookup.TryGetValue(asset.Name, out var sha) ? sha : null;
        }

        private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
        {
            await using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            var bytes = await sha.ComputeHashAsync(stream, ct).ConfigureAwait(false);
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
