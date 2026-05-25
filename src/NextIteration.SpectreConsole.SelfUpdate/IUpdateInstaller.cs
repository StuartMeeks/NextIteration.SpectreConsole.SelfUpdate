namespace NextIteration.SpectreConsole.SelfUpdate
{
    /// <summary>
    /// Downloads a release, runs the verifier pipeline, extracts the archive,
    /// and atomically swaps the new files into the install directory. The
    /// previous install is moved to a sibling <c>.old/</c> directory; both it
    /// and the <c>.update/</c> staging tree are deleted on the next startup
    /// via <see cref="CleanupOldInstall"/>.
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
        /// <param name="onConflict">
        /// Optional resolver invoked when a new release entry lands on a
        /// path covered by <see cref="SelfUpdaterOptions.PreservePaths"/>.
        /// <see langword="null"/> (default) means
        /// <see cref="UpdateConflictResolution.KeepExisting"/> — the user's
        /// file is left in place and the new release's copy is discarded
        /// for that path. Resolvers are called once per conflicting entry,
        /// in deterministic order, and may return different decisions per
        /// entry.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        Task InstallAsync(
            RemoteRelease release,
            IProgress<UpdateProgressEvent>? progress = null,
            Func<UpdateConflict, CancellationToken, Task<UpdateConflictResolution>>? onConflict = null,
            CancellationToken ct = default);

        /// <summary>
        /// Idempotent. Call this once at the very start of the CLI's
        /// <c>Main</c> to delete any <c>.old/</c> or <c>.update/</c>
        /// directories left behind by a previous successful update — the
        /// running new binary is sufficient proof both the swap and the
        /// extraction completed. The startup pass is the canonical
        /// retry path for OneDrive / antivirus contention that defeated
        /// cleanup at install time. Safe to call when neither directory
        /// exists; failures are swallowed (will retry next startup).
        /// </summary>
        void CleanupOldInstall();
    }
}
