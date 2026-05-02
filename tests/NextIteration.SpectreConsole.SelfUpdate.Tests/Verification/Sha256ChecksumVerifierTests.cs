using System.Security.Cryptography;
using System.Text;

using NextIteration.SpectreConsole.SelfUpdate.Tests.Infrastructure;
using NextIteration.SpectreConsole.SelfUpdate.Verification;

using Xunit;

namespace NextIteration.SpectreConsole.SelfUpdate.Tests.Verification
{
    public sealed class Sha256ChecksumVerifierTests
    {
        [Fact]
        public async Task VerifyAsync_when_metadata_sha256_matches_passes()
        {
            using var dir = new TempDir();
            var path = dir.Combine("payload.bin");
            var bytes = new byte[] { 1, 2, 3, 4 };
            await File.WriteAllBytesAsync(path, bytes);

            var sha = ComputeSha256Hex(bytes);
            var asset = MakeAsset("payload.bin", new Dictionary<string, string> { ["sha256"] = sha });
            var release = MakeRelease(new[] { asset });

            var verifier = new Sha256ChecksumVerifier(new FakeUpdateSource());
            await verifier.VerifyAsync(path, release, asset, CancellationToken.None);
        }

        [Fact]
        public async Task VerifyAsync_when_metadata_sha256_mismatches_throws()
        {
            using var dir = new TempDir();
            var path = dir.Combine("payload.bin");
            await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3, 4 });

            var asset = MakeAsset("payload.bin", new Dictionary<string, string>
            {
                ["sha256"] = "deadbeef".PadRight(64, '0'),
            });
            var release = MakeRelease(new[] { asset });

            var verifier = new Sha256ChecksumVerifier(new FakeUpdateSource());

            await Assert.ThrowsAsync<UpdateException>(() =>
                verifier.VerifyAsync(path, release, asset, CancellationToken.None));
        }

        [Fact]
        public async Task VerifyAsync_when_no_metadata_falls_back_to_manifest_asset()
        {
            using var dir = new TempDir();
            var path = dir.Combine("payload.bin");
            var bytes = new byte[] { 5, 6, 7 };
            await File.WriteAllBytesAsync(path, bytes);

            var asset = MakeAsset("payload.bin", new Dictionary<string, string>());
            var manifestAsset = MakeAsset("SHA256SUMS.txt", new Dictionary<string, string>());
            var release = MakeRelease(new[] { asset, manifestAsset });

            var sha = ComputeSha256Hex(bytes);
            var manifestText = $"{sha}  payload.bin\n";

            var source = new FakeUpdateSource
            {
                AssetBytes = a =>
                    a.Name == "SHA256SUMS.txt"
                        ? Encoding.UTF8.GetBytes(manifestText)
                        : throw new InvalidOperationException("Verifier should not download anything other than the manifest."),
            };

            var verifier = new Sha256ChecksumVerifier(source);
            await verifier.VerifyAsync(path, release, asset, CancellationToken.None);
        }

        [Fact]
        public async Task VerifyAsync_when_manifest_asset_missing_throws()
        {
            using var dir = new TempDir();
            var path = dir.Combine("payload.bin");
            await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3 });

            var asset = MakeAsset("payload.bin", new Dictionary<string, string>());
            var release = MakeRelease(new[] { asset });   // no SHA256SUMS asset

            var verifier = new Sha256ChecksumVerifier(new FakeUpdateSource());

            await Assert.ThrowsAsync<UpdateException>(() =>
                verifier.VerifyAsync(path, release, asset, CancellationToken.None));
        }

        private static string ComputeSha256Hex(byte[] bytes)
        {
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }

        private static ReleaseAsset MakeAsset(string name, IReadOnlyDictionary<string, string> metadata) =>
            new(
                Name: name,
                DownloadUrl: new Uri("https://example.com/" + name),
                SizeBytes: null,
                ContentType: null,
                Metadata: metadata);

        private static RemoteRelease MakeRelease(IReadOnlyList<ReleaseAsset> assets) =>
            new(Tag: "v1.0.0", Channel: null, ReleaseNotesUrl: null, Assets: assets, PublishedAt: DateTimeOffset.UtcNow);
    }
}
