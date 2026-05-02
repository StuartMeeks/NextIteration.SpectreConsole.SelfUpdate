namespace NextIteration.SpectreConsole.SelfUpdate
{
    /// <summary>
    /// The package's primary extension point. Implement this interface to
    /// teach the self-updater about a non-default release backend (e.g. an
    /// internal artifact registry, an Azure Blob manifest, a custom HTTP
    /// API). Built-in implementations:
    /// <list type="bullet">
    ///   <item><description><c>HttpGitHubReleaseSource</c> — public GitHub Releases via HttpClient (default).</description></item>
    ///   <item><description><c>GhCliReleaseSource</c> — private GitHub repos via the <c>gh</c> CLI.</description></item>
    ///   <item><description><c>HttpManifestSource</c> — a generic JSON manifest hosted on any HTTPS endpoint.</description></item>
    /// </list>
    /// </summary>
    public interface IUpdateSource
    {
        /// <summary>
        /// Resolve the latest release on the configured channel, or
        /// <see langword="null"/> when no release exists or the source is
        /// unreachable. Implementations should <b>swallow transient failures</b>
        /// (offline, HTTP 5xx, rate-limit) and return <see langword="null"/>
        /// rather than throw — the checker treats null as "no upgrade
        /// information; try again next tick".
        /// </summary>
        /// <param name="channel">
        /// Optional channel filter. <see langword="null"/> means the source's
        /// default channel (typically the latest non-prerelease tag).
        /// </param>
        /// <param name="ct">Cancellation token honoured for both DNS and stream reads.</param>
        Task<RemoteRelease?> GetLatestAsync(string? channel, CancellationToken ct);

        /// <summary>
        /// Stream a single release asset to <paramref name="destination"/>.
        /// Should write nothing on failure and propagate the underlying
        /// exception to the caller (the installer surfaces it as an
        /// <see cref="UpdateException"/> with context).
        /// </summary>
        /// <param name="asset">The asset to fetch.</param>
        /// <param name="destination">
        /// Open, writable stream the implementation copies bytes into. Not
        /// disposed by the implementation — the caller owns the stream's
        /// lifetime so it can be re-read for verification.
        /// </param>
        /// <param name="progress">
        /// Optional progress sink. Implementations should report at least
        /// every few percent of the download (or every few hundred KB for
        /// streams of unknown length).
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        Task DownloadAssetAsync(
            ReleaseAsset asset,
            Stream destination,
            IProgress<DownloadProgress>? progress,
            CancellationToken ct);
    }
}
