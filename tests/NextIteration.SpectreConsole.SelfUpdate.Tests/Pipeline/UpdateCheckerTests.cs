using NextIteration.SpectreConsole.SelfUpdate.Pipeline;
using NextIteration.SpectreConsole.SelfUpdate.Tests.Infrastructure;

using Xunit;

namespace NextIteration.SpectreConsole.SelfUpdate.Tests.Pipeline
{
    public sealed class UpdateCheckerTests
    {
        [Fact]
        public async Task CheckAsync_when_opt_out_env_set_returns_null()
        {
            using var dir = new TempDir();
            var opts = new SelfUpdaterOptions { AppName = "myapp", CacheDirectory = dir.Path, SkipVersionPredicate = null };
            var source = new FakeUpdateSource { LatestForChannel = TestRelease("v1.2.3") };
            var checker = new UpdateChecker(
                opts, source,
                currentVersionResolver: () => "1.0.0",
                envResolver: name => name == "MYAPP_SKIP_UPDATE_CHECK" ? "1" : null,
                utcNow: () => DateTimeOffset.UtcNow);

            var info = await checker.CheckAsync();

            Assert.Null(info);
            Assert.Equal(0, source.GetLatestCallCount);
        }

        [Fact]
        public async Task CheckAsync_when_dev_version_predicate_matches_returns_null()
        {
            using var dir = new TempDir();
            var opts = new SelfUpdaterOptions
            {
                AppName = "myapp",
                CacheDirectory = dir.Path,
                SkipVersionPredicate = v => v == "1.0.0",
            };
            var source = new FakeUpdateSource { LatestForChannel = TestRelease("v1.2.3") };
            var checker = NewChecker(opts, source, "1.0.0");

            var info = await checker.CheckAsync();

            Assert.Null(info);
            Assert.Equal(0, source.GetLatestCallCount);
        }

        [Fact]
        public async Task CheckAsync_when_cache_fresh_skips_source()
        {
            using var dir = new TempDir();
            var opts = new SelfUpdaterOptions { AppName = "myapp", CacheDirectory = dir.Path, SkipVersionPredicate = null, CacheTtl = TimeSpan.FromHours(1) };
            var source = new FakeUpdateSource { LatestForChannel = TestRelease("v9.9.9") };
            var nowAnchor = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
            var checker = NewChecker(opts, source, "1.0.0", utcNow: () => nowAnchor);

            // First call writes the cache.
            var first = await checker.CheckAsync();
            Assert.NotNull(first);
            Assert.Equal(1, source.GetLatestCallCount);

            // Second call with the same now should reuse cache.
            var second = await checker.CheckAsync();
            Assert.NotNull(second);
            Assert.Equal(1, source.GetLatestCallCount);
        }

        [Fact]
        public async Task CheckAsync_when_cache_expired_re_fetches()
        {
            using var dir = new TempDir();
            var opts = new SelfUpdaterOptions { AppName = "myapp", CacheDirectory = dir.Path, SkipVersionPredicate = null, CacheTtl = TimeSpan.FromMinutes(5) };
            var source = new FakeUpdateSource { LatestForChannel = TestRelease("v1.0.5") };
            var anchor = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
            DateTimeOffset now = anchor;
            var checker = NewChecker(opts, source, "1.0.0", utcNow: () => now);

            await checker.CheckAsync();
            now = anchor.AddMinutes(10);   // expire the cache
            await checker.CheckAsync();

            Assert.Equal(2, source.GetLatestCallCount);
        }

        [Fact]
        public async Task CheckAsync_when_channel_changes_invalidates_cache()
        {
            using var dir = new TempDir();
            var opts = new SelfUpdaterOptions { AppName = "myapp", CacheDirectory = dir.Path, SkipVersionPredicate = null };
            var source = new FakeUpdateSource
            {
                LatestSelector = c => TestRelease(c == "beta" ? "v1.5.0-beta.1" : "v1.4.0"),
            };

            var checker = NewChecker(opts, source, "1.0.0");
            await checker.CheckAsync();   // populates cache for channel=null

            opts.Channel = "beta";
            await checker.CheckAsync();   // should refetch because channel mismatch

            Assert.Equal(2, source.GetLatestCallCount);
        }

        [Fact]
        public async Task CheckAsync_returns_update_available_when_remote_is_newer()
        {
            using var dir = new TempDir();
            var opts = new SelfUpdaterOptions { AppName = "myapp", CacheDirectory = dir.Path, SkipVersionPredicate = null };
            var source = new FakeUpdateSource { LatestForChannel = TestRelease("v1.0.5") };
            var checker = NewChecker(opts, source, "1.0.0");

            var info = await checker.CheckAsync();

            Assert.NotNull(info);
            Assert.True(info!.IsUpdateAvailable);
            Assert.Equal("1.0.0", info.CurrentVersion);
            Assert.Equal("v1.0.5", info.LatestTag);
        }

        [Fact]
        public async Task CheckAsync_override_true_does_not_reuse_default_cache_entry()
        {
            using var dir = new TempDir();
            var opts = new SelfUpdaterOptions { AppName = "myapp", CacheDirectory = dir.Path, SkipVersionPredicate = null };
            var source = new FakeUpdateSource
            {
                LatestSelector = _ => TestRelease("v1.5.0"),
            };
            var checker = NewChecker(opts, source, "1.0.0");

            // First call without override populates the default-prerelease cache.
            await checker.CheckAsync();
            Assert.Equal(1, source.GetLatestCallCount);

            // Second call with override=true must not reuse the default cache.
            await checker.CheckAsync(includePrereleasesOverride: true);
            Assert.Equal(2, source.GetLatestCallCount);
            Assert.True(source.LastIncludePrereleasesOverride);
        }

        [Fact]
        public async Task CheckAsync_returns_no_update_when_remote_is_same()
        {
            using var dir = new TempDir();
            var opts = new SelfUpdaterOptions { AppName = "myapp", CacheDirectory = dir.Path, SkipVersionPredicate = null };
            var source = new FakeUpdateSource { LatestForChannel = TestRelease("v1.0.0") };
            var checker = NewChecker(opts, source, "1.0.0");

            var info = await checker.CheckAsync();

            Assert.NotNull(info);
            Assert.False(info!.IsUpdateAvailable);
        }

        private static UpdateChecker NewChecker(
            SelfUpdaterOptions opts,
            FakeUpdateSource source,
            string currentVersion,
            Func<DateTimeOffset>? utcNow = null)
        {
            return new UpdateChecker(
                opts, source,
                currentVersionResolver: () => currentVersion,
                envResolver: _ => null,
                utcNow: utcNow ?? (() => DateTimeOffset.UtcNow));
        }

        private static RemoteRelease TestRelease(string tag) =>
            new(tag,
                Channel: null,
                ReleaseNotesUrl: new Uri("https://example.com/releases/" + tag),
                Assets: Array.Empty<ReleaseAsset>(),
                PublishedAt: DateTimeOffset.UtcNow);
    }
}
