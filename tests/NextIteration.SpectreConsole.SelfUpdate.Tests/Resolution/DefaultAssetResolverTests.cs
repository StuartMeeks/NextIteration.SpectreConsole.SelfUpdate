using NextIteration.SpectreConsole.SelfUpdate.Resolution;

using Xunit;

namespace NextIteration.SpectreConsole.SelfUpdate.Tests.Resolution
{
    public sealed class DefaultAssetResolverTests
    {
        [Fact]
        public void Resolve_picks_exact_versioned_match_first()
        {
            var release = ReleaseWith("v1.4.2",
                "myapp-v1.4.2-linux-x64.tar.gz",
                "myapp-v1.4.2-osx-arm64.tar.gz",
                "myapp-v1.4.2-win-x64.zip",
                "extras-linux-x64.zip");
            var resolver = new DefaultAssetResolver("myapp");

            var match = resolver.Resolve(release, "linux-x64");

            Assert.NotNull(match);
            Assert.Equal("myapp-v1.4.2-linux-x64.tar.gz", match!.Name);
        }

        [Fact]
        public void Resolve_falls_back_to_unversioned_match()
        {
            var release = ReleaseWith("v1.4.2",
                "myapp-linux-x64.tar.gz",
                "myapp-osx-arm64.tar.gz");
            var resolver = new DefaultAssetResolver("myapp");

            var match = resolver.Resolve(release, "linux-x64");

            Assert.Equal("myapp-linux-x64.tar.gz", match?.Name);
        }

        [Fact]
        public void Resolve_falls_back_to_zip_when_targz_missing()
        {
            var release = ReleaseWith("v1.4.2",
                "myapp-v1.4.2-linux-x64.zip",
                "myapp-v1.4.2-osx-arm64.zip");
            var resolver = new DefaultAssetResolver("myapp");

            var match = resolver.Resolve(release, "linux-x64");

            Assert.Equal("myapp-v1.4.2-linux-x64.zip", match?.Name);
        }

        [Fact]
        public void Resolve_falls_back_to_rid_only_match_as_last_resort()
        {
            var release = ReleaseWith("v1.4.2",
                "different-name-linux-x64.tar.gz",
                "another-osx-arm64.tar.gz");
            var resolver = new DefaultAssetResolver("myapp");

            var match = resolver.Resolve(release, "linux-x64");

            Assert.Equal("different-name-linux-x64.tar.gz", match?.Name);
        }

        [Fact]
        public void Resolve_when_osx_arm64_missing_falls_back_to_osx_x64()
        {
            var release = ReleaseWith("v1.4.2",
                "myapp-v1.4.2-osx-x64.tar.gz",
                "myapp-v1.4.2-linux-x64.tar.gz");
            var resolver = new DefaultAssetResolver("myapp");

            var match = resolver.Resolve(release, "osx-arm64");

            Assert.Equal("myapp-v1.4.2-osx-x64.tar.gz", match?.Name);
        }

        [Fact]
        public void Resolve_when_osx_arm64_missing_falls_back_to_universal_osx()
        {
            var release = ReleaseWith("v1.4.2",
                "myapp-v1.4.2-osx.tar.gz",
                "myapp-v1.4.2-linux-x64.tar.gz");
            var resolver = new DefaultAssetResolver("myapp");

            var match = resolver.Resolve(release, "osx-arm64");

            Assert.Equal("myapp-v1.4.2-osx.tar.gz", match?.Name);
        }

        [Fact]
        public void Resolve_returns_null_when_no_archive_matches()
        {
            var release = ReleaseWith("v1.4.2",
                "myapp-v1.4.2-osx-arm64.tar.gz",
                "myapp-v1.4.2-osx-x64.tar.gz");
            var resolver = new DefaultAssetResolver("myapp");

            var match = resolver.Resolve(release, "linux-arm64");

            Assert.Null(match);
        }

        [Fact]
        public void Resolve_is_case_insensitive_on_filenames()
        {
            var release = ReleaseWith("v1.4.2", "MyApp-v1.4.2-LINUX-X64.ZIP");
            var resolver = new DefaultAssetResolver("myapp");

            var match = resolver.Resolve(release, "linux-x64");

            Assert.NotNull(match);
        }

        [Fact]
        public void Resolve_when_tag_lacks_v_prefix_still_matches()
        {
            var release = ReleaseWith("1.4.2", "myapp-1.4.2-linux-x64.tar.gz");
            var resolver = new DefaultAssetResolver("myapp");

            var match = resolver.Resolve(release, "linux-x64");

            Assert.Equal("myapp-1.4.2-linux-x64.tar.gz", match?.Name);
        }

        [Fact]
        public void Resolve_throws_on_null_release()
        {
            var resolver = new DefaultAssetResolver("myapp");
            Assert.Throws<ArgumentNullException>(() => resolver.Resolve(null!, "linux-x64"));
        }

        [Fact]
        public void Constructor_throws_on_blank_app_name()
        {
            Assert.Throws<ArgumentException>(() => new DefaultAssetResolver(""));
        }

        private static RemoteRelease ReleaseWith(string tag, params string[] assetNames)
        {
            var assets = assetNames
                .Select(n => new ReleaseAsset(
                    Name: n,
                    DownloadUrl: new Uri("https://example.com/" + n),
                    SizeBytes: 1024,
                    ContentType: "application/octet-stream",
                    Metadata: new Dictionary<string, string>()))
                .ToArray();
            return new RemoteRelease(tag, Channel: null, ReleaseNotesUrl: null, Assets: assets, PublishedAt: DateTimeOffset.UtcNow);
        }
    }
}
