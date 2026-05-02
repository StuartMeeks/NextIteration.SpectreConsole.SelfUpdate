namespace NextIteration.SpectreConsole.SelfUpdate
{
    /// <summary>
    /// Downloads a release, runs the verifier pipeline, extracts the archive,
    /// and atomically swaps the new files into the install directory. The
    /// previous install is moved to a sibling <c>.old/</c> directory and
    /// deleted on the next startup via <see cref="CleanupOldInstall"/>.
    /// </summary>
    public interface IUpdateInstaller
    {
        /// <summary>The directory the running CLI lives in — the swap target.</summary>
        string InstallDirectory { get; }

        /// <summary>
        /// Apply the given release. Throws <see cref="UpdateException"/>
        /// when any pipeline stage fails; on success returns once all files
        /// are in place. The caller is responsible for telling the user
        /// they need to re-run the CLI to pick up the new version (the
        /// installer cannot restart itself reliably across OS/sandbox
        /// boundaries).
        /// </summary>
        /// <param name="release">The resolved release to install.</param>
        /// <param name="progress">Optional progress sink for stage-level events.</param>
        /// <param name="ct">Cancellation token.</param>
        Task InstallAsync(
            RemoteRelease release,
            IProgress<UpdateProgressEvent>? progress = null,
            CancellationToken ct = default);

        /// <summary>
        /// Idempotent. Call this once at the very start of the CLI's
        /// <c>Main</c> to delete any <c>.old/</c> directory left behind by a
        /// previous successful update — the running new binary is sufficient
        /// proof the swap completed. Safe to call when no <c>.old/</c>
        /// exists; failures are swallowed (will retry next startup).
        /// </summary>
        void CleanupOldInstall();
    }
}
