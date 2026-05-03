namespace NextIteration.SpectreConsole.SelfUpdate
{
    /// <summary>
    /// Per-file decision returned by a conflict resolver when a new release
    /// ships an entry whose path matches one of
    /// <see cref="SelfUpdaterOptions.PreservePaths"/>.
    /// </summary>
    public enum UpdateConflictResolution
    {
        /// <summary>
        /// Keep the user's existing file untouched. The new release's copy
        /// is silently discarded for this path. Safe default — chosen when
        /// no resolver is supplied.
        /// </summary>
        KeepExisting,

        /// <summary>
        /// Replace the user's existing file with the new release's copy.
        /// The previous content is moved to <c>.old/</c> as part of the
        /// normal swap.
        /// </summary>
        UseNew,
    }
}
