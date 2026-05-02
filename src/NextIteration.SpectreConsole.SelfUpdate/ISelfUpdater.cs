namespace NextIteration.SpectreConsole.SelfUpdate
{
    /// <summary>
    /// The high-level façade most consumers interact with. Composes the
    /// registered <see cref="IUpdateChecker"/> and
    /// <see cref="IUpdateInstaller"/> behind a single service. Resolved from
    /// DI after calling <c>services.AddSelfUpdater(...)</c>.
    /// </summary>
    public interface ISelfUpdater
    {
        /// <summary>
        /// Non-blocking probe — see <see cref="IUpdateChecker.CheckAsync"/>.
        /// </summary>
        Task<UpdateInfo?> CheckAsync(CancellationToken ct = default);

        /// <summary>
        /// Resolve the latest release on the configured channel, download
        /// it, run the verifier pipeline, extract the archive, and swap the
        /// new files into the install directory. Throws
        /// <see cref="UpdateException"/> when no release is available or
        /// when any pipeline stage fails.
        /// </summary>
        /// <param name="progress">Optional progress sink.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <remarks>
        /// v0.1 always installs the latest release on the configured
        /// channel — there is no "install a specific older tag" overload.
        /// Tag-pinned installs may arrive in a later version once every
        /// source can resolve a release by tag.
        /// </remarks>
        Task InstallAsync(
            IProgress<UpdateProgressEvent>? progress = null,
            CancellationToken ct = default);
    }
}
