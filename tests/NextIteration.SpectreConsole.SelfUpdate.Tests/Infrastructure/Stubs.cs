namespace NextIteration.SpectreConsole.SelfUpdate.Tests.Infrastructure
{
    /// <summary>Stub <see cref="ISelfUpdater"/> for command tests.</summary>
    internal sealed class StubSelfUpdater : ISelfUpdater
    {
        public Func<CancellationToken, Task<UpdateInfo?>>? CheckImpl { get; set; }
        public Func<CancellationToken, Task<RemoteRelease?>>? GetLatestImpl { get; set; }
        public Func<IProgress<UpdateProgressEvent>?, CancellationToken, Task>? InstallImpl { get; set; }
        public Func<RemoteRelease, IProgress<UpdateProgressEvent>?, Func<UpdateConflict, CancellationToken, Task<UpdateConflictResolution>>?, CancellationToken, Task>? InstallReleaseImpl { get; set; }
        public bool? LastIncludePrereleasesOverride { get; private set; }

        public Task<UpdateInfo?> CheckAsync(CancellationToken ct = default) =>
            CheckImpl?.Invoke(ct) ?? Task.FromResult<UpdateInfo?>(null);

        public Task<RemoteRelease?> GetLatestReleaseAsync(CancellationToken ct = default) =>
            GetLatestReleaseAsync(includePrereleasesOverride: null, ct);

        public Task<RemoteRelease?> GetLatestReleaseAsync(bool? includePrereleasesOverride, CancellationToken ct = default)
        {
            LastIncludePrereleasesOverride = includePrereleasesOverride;
            return GetLatestImpl?.Invoke(ct) ?? Task.FromResult<RemoteRelease?>(null);
        }

        public Task InstallAsync(
            RemoteRelease release,
            IProgress<UpdateProgressEvent>? progress = null,
            Func<UpdateConflict, CancellationToken, Task<UpdateConflictResolution>>? onConflict = null,
            CancellationToken ct = default) =>
            InstallReleaseImpl?.Invoke(release, progress, onConflict, ct) ?? Task.CompletedTask;

        public Task InstallAsync(IProgress<UpdateProgressEvent>? progress = null, CancellationToken ct = default) =>
            InstallImpl?.Invoke(progress, ct) ?? Task.CompletedTask;
    }

    /// <summary>Stub <see cref="IUpdateChecker"/> for command tests.</summary>
    internal sealed class StubUpdateChecker : IUpdateChecker
    {
        public string? CurrentVersion { get; set; } = "1.0.0";
        public Func<CancellationToken, Task<UpdateInfo?>>? CheckImpl { get; set; }
        public bool? LastIncludePrereleasesOverride { get; private set; }

        public Task<UpdateInfo?> CheckAsync(CancellationToken ct = default) =>
            CheckAsync(includePrereleasesOverride: null, ct);

        public Task<UpdateInfo?> CheckAsync(bool? includePrereleasesOverride, CancellationToken ct = default)
        {
            LastIncludePrereleasesOverride = includePrereleasesOverride;
            return CheckImpl?.Invoke(ct) ?? Task.FromResult<UpdateInfo?>(null);
        }

        public string? GetCurrentVersion() => CurrentVersion;
    }

    /// <summary>Stub <see cref="IUpdateInstaller"/> for command tests.</summary>
    internal sealed class StubUpdateInstaller : IUpdateInstaller
    {
        public string InstallDirectory { get; set; } = "/tmp/install";

        public Task InstallAsync(
            RemoteRelease release,
            IProgress<UpdateProgressEvent>? progress = null,
            Func<UpdateConflict, CancellationToken, Task<UpdateConflictResolution>>? onConflict = null,
            CancellationToken ct = default) => Task.CompletedTask;

        public void CleanupOldInstall() { }
    }
}
