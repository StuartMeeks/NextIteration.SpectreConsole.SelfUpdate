namespace NextIteration.SpectreConsole.SelfUpdate.Pipeline
{
    /// <summary>
    /// Default <see cref="ISelfUpdater"/>. Composes
    /// <see cref="IUpdateChecker"/> (for the cheap "is there a newer
    /// release?" probe) with <see cref="IUpdateSource"/> +
    /// <see cref="IUpdateInstaller"/> (for the actual download/swap).
    /// </summary>
    internal sealed class SelfUpdater : ISelfUpdater
    {
        private readonly IUpdateChecker _checker;
        private readonly IUpdateSource _source;
        private readonly IUpdateInstaller _installer;
        private readonly SelfUpdaterOptions _options;

        public SelfUpdater(
            IUpdateChecker checker,
            IUpdateSource source,
            IUpdateInstaller installer,
            SelfUpdaterOptions options)
        {
            ArgumentNullException.ThrowIfNull(checker);
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(installer);
            ArgumentNullException.ThrowIfNull(options);

            _checker = checker;
            _source = source;
            _installer = installer;
            _options = options;
        }

        public Task<UpdateInfo?> CheckAsync(CancellationToken ct = default) =>
            _checker.CheckAsync(ct);

        public Task<RemoteRelease?> GetLatestReleaseAsync(CancellationToken ct = default) =>
            _source.GetLatestAsync(_options.Channel, ct);

        public Task InstallAsync(
            RemoteRelease release,
            IProgress<UpdateProgressEvent>? progress = null,
            Func<UpdateConflict, CancellationToken, Task<UpdateConflictResolution>>? onConflict = null,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(release);
            return _installer.InstallAsync(release, progress, onConflict, ct);
        }

        public async Task InstallAsync(IProgress<UpdateProgressEvent>? progress = null, CancellationToken ct = default)
        {
            var release = await _source.GetLatestAsync(_options.Channel, ct).ConfigureAwait(false);
            if (release is null)
            {
                throw new UpdateException(
                    "No release is available from the configured update source. The source either returned null or is currently unreachable.");
            }
            await _installer.InstallAsync(release, progress, onConflict: null, ct).ConfigureAwait(false);
        }
    }
}
