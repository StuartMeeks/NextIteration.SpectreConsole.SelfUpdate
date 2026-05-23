namespace NextIteration.SpectreConsole.SelfUpdate
{
    /// <summary>
    /// Read-only "is there a newer release?" probe. The default
    /// implementation queries the registered <see cref="IUpdateSource"/>,
    /// caches the result in a per-user file (configurable TTL) and returns
    /// the cached value while it's fresh — so calling this on every CLI
    /// invocation is cheap.
    /// </summary>
    public interface IUpdateChecker
    {
        /// <summary>
        /// Resolve the current-vs-latest comparison. Returns
        /// <see langword="null"/> when the check is suppressed (opt-out env
        /// var, dev-build version) or the source is unreachable; otherwise
        /// returns an <see cref="UpdateInfo"/> with
        /// <see cref="UpdateInfo.IsUpdateAvailable"/> populated.
        /// </summary>
        Task<UpdateInfo?> CheckAsync(CancellationToken ct = default);

        /// <summary>
        /// Per-invocation variant of <see cref="CheckAsync(CancellationToken)"/>
        /// that lets the caller override
        /// <see cref="SelfUpdaterOptions.IncludePrereleases"/> for one call
        /// (used by the <c>update check --prerelease</c> CLI flag).
        /// <see langword="null"/> defers to the configured option;
        /// <see langword="true"/>/<see langword="false"/> force inclusion or
        /// exclusion. The default-interface implementation drops the override
        /// and delegates to the base overload so existing custom checkers
        /// continue to compile.
        /// </summary>
        Task<UpdateInfo?> CheckAsync(bool? includePrereleasesOverride, CancellationToken ct = default) =>
            CheckAsync(ct);

        /// <summary>
        /// The running CLI's version, read from
        /// <see cref="System.Reflection.AssemblyInformationalVersionAttribute"/>
        /// on the entry assembly with any <c>+sha</c> build metadata
        /// stripped. Returns <see langword="null"/> only when the attribute
        /// is absent (e.g. <c>dotnet run</c> on an unbuilt project). The
        /// <see cref="SelfUpdaterOptions.SkipVersionPredicate"/> is
        /// <b>not</b> consulted here — predicate-skipped versions are still
        /// reported so commands can display them; the predicate suppresses
        /// the <i>check</i> inside <see cref="CheckAsync(CancellationToken)"/>, not the
        /// displayed version.
        /// </summary>
        string? GetCurrentVersion();
    }
}
