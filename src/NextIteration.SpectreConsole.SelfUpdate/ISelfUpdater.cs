namespace NextIteration.SpectreConsole.SelfUpdate
{
    /// <summary>
    /// The high-level façade most consumers interact with. Composes the
    /// registered <see cref="IUpdateChecker"/>,
    /// <see cref="IUpdateSource"/>, and <see cref="IUpdateInstaller"/>
    /// behind a single service. Resolved from DI after calling
    /// <c>services.AddSelfUpdater(...)</c>.
    /// </summary>
    public interface ISelfUpdater
    {
        /// <summary>
        /// Non-blocking probe — see <see cref="IUpdateChecker.CheckAsync"/>.
        /// </summary>
        Task<UpdateInfo?> CheckAsync(CancellationToken ct = default);

        /// <summary>
        /// Hit the configured <see cref="IUpdateSource"/> for the current
        /// latest release on the configured channel. Returns
        /// <see langword="null"/> when nothing is available. Use this when
        /// you want to display "what would be installed?" to the user
        /// before calling <see cref="InstallAsync(RemoteRelease, IProgress{UpdateProgressEvent}, CancellationToken)"/>
        /// — the same release instance can be passed to install so the
        /// displayed and installed versions are guaranteed to match (no
        /// TOCTOU window between display and install).
        /// </summary>
        Task<RemoteRelease?> GetLatestReleaseAsync(CancellationToken ct = default);

        /// <summary>
        /// Install the supplied release: download, run the verifier
        /// pipeline, extract the archive, and swap the new files into the
        /// install directory. Throws <see cref="UpdateException"/> on any
        /// failure. Prefer this overload when you've already shown the
        /// user a specific release — passing it back avoids the second
        /// source query the parameterless overload performs.
        /// </summary>
        Task InstallAsync(
            RemoteRelease release,
            IProgress<UpdateProgressEvent>? progress = null,
            CancellationToken ct = default);

        /// <summary>
        /// Convenience: query the source for the latest release on the
        /// configured channel and install it. Throws
        /// <see cref="UpdateException"/> when no release is available or
        /// when any pipeline stage fails. <b>Has a TOCTOU window</b>: the
        /// release returned by the source here may differ from the one a
        /// prior <see cref="CheckAsync"/> reported. For interactive UIs,
        /// prefer the <see cref="GetLatestReleaseAsync"/> +
        /// <see cref="InstallAsync(RemoteRelease, IProgress{UpdateProgressEvent}, CancellationToken)"/>
        /// pair so the user confirms exactly the release that gets
        /// installed.
        /// </summary>
        Task InstallAsync(
            IProgress<UpdateProgressEvent>? progress = null,
            CancellationToken ct = default);
    }
}
