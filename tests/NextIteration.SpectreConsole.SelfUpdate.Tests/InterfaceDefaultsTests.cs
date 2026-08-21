using Xunit;

namespace NextIteration.SpectreConsole.SelfUpdate.Tests
{
    /// <summary>
    /// Locks in the 1.0.0 interface shape: the override-carrying method is the
    /// abstract one, and the convenience overload is the default interface
    /// implementation that delegates to it. Before 1.0.0 the relationship ran
    /// the other way, so an implementation that supplied only the abstract
    /// member compiled cleanly and then silently discarded the caller's
    /// prerelease override.
    /// </summary>
    public sealed class InterfaceDefaultsTests
    {
        // Implements ONLY the abstract member of each interface. If the
        // override-carrying method ever stops being the abstract one, this type
        // fails to compile — which is the guarantee, not the assertions below.
        private sealed class MinimalSource : IUpdateSource
        {
            public bool? LastOverride { get; private set; }
            public int Calls { get; private set; }

            public Task<RemoteRelease?> GetLatestAsync(string? channel, bool? includePrereleasesOverride, CancellationToken ct)
            {
                LastOverride = includePrereleasesOverride;
                Calls++;
                return Task.FromResult<RemoteRelease?>(null);
            }

            public Task DownloadAssetAsync(ReleaseAsset asset, Stream destination, IProgress<DownloadProgress>? progress, CancellationToken ct) =>
                Task.CompletedTask;
        }

        private sealed class MinimalChecker : IUpdateChecker
        {
            public bool? LastOverride { get; private set; }

            public Task<UpdateInfo?> CheckAsync(bool? includePrereleasesOverride, CancellationToken ct = default)
            {
                LastOverride = includePrereleasesOverride;
                return Task.FromResult<UpdateInfo?>(null);
            }

            public string? GetCurrentVersion() => "1.0.0";
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        [InlineData(null)]
        public async Task Source_override_reaches_a_minimal_implementation(bool? requested)
        {
            var source = new MinimalSource();

            await ((IUpdateSource)source).GetLatestAsync("stable", requested, TestContext.Current.CancellationToken);

            Assert.Equal(requested, source.LastOverride);
        }

        [Fact]
        public async Task Source_convenience_overload_passes_null_and_still_dispatches()
        {
            var source = new MinimalSource();

            // The two-argument overload is the default interface implementation,
            // so it must route through the single abstract member.
            await ((IUpdateSource)source).GetLatestAsync("stable", TestContext.Current.CancellationToken);

            Assert.Equal(1, source.Calls);
            Assert.Null(source.LastOverride);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        [InlineData(null)]
        public async Task Checker_override_reaches_a_minimal_implementation(bool? requested)
        {
            var checker = new MinimalChecker();

            await ((IUpdateChecker)checker).CheckAsync(requested, TestContext.Current.CancellationToken);

            Assert.Equal(requested, checker.LastOverride);
        }

        [Fact]
        public async Task Checker_convenience_overload_passes_null()
        {
            var checker = new MinimalChecker();

            await ((IUpdateChecker)checker).CheckAsync(TestContext.Current.CancellationToken);

            Assert.Null(checker.LastOverride);
        }
    }
}
