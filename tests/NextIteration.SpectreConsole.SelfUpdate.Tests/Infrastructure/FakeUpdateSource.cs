namespace NextIteration.SpectreConsole.SelfUpdate.Tests.Infrastructure
{
    /// <summary>
    /// Stub <see cref="IUpdateSource"/> the tests configure inline. No
    /// network, no process boundaries, no Moq.
    /// </summary>
    internal sealed class FakeUpdateSource : IUpdateSource
    {
        public RemoteRelease? LatestForChannel { get; set; }
        public Func<string?, RemoteRelease?>? LatestSelector { get; set; }
        public Func<ReleaseAsset, byte[]>? AssetBytes { get; set; }
        public int GetLatestCallCount { get; private set; }
        public string? LastChannelRequested { get; private set; }
        public bool? LastIncludePrereleasesOverride { get; private set; }

        public Task<RemoteRelease?> GetLatestAsync(string? channel, CancellationToken ct) =>
            GetLatestAsync(channel, includePrereleasesOverride: null, ct);

        public Task<RemoteRelease?> GetLatestAsync(string? channel, bool? includePrereleasesOverride, CancellationToken ct)
        {
            GetLatestCallCount++;
            LastChannelRequested = channel;
            LastIncludePrereleasesOverride = includePrereleasesOverride;
            var result = LatestSelector?.Invoke(channel) ?? LatestForChannel;
            return Task.FromResult(result);
        }

        public async Task DownloadAssetAsync(ReleaseAsset asset, Stream destination, IProgress<DownloadProgress>? progress, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(asset);
            ArgumentNullException.ThrowIfNull(destination);
            var bytes = AssetBytes?.Invoke(asset)
                ?? throw new InvalidOperationException($"FakeUpdateSource has no bytes configured for asset '{asset.Name}'.");
            progress?.Report(new DownloadProgress(0, bytes.LongLength));
            await destination.WriteAsync(bytes, ct).ConfigureAwait(false);
            progress?.Report(new DownloadProgress(bytes.LongLength, bytes.LongLength));
        }
    }
}
