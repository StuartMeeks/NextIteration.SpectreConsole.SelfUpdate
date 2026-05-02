namespace NextIteration.SpectreConsole.SelfUpdate
{
    /// <summary>
    /// The result of <see cref="ISelfUpdater.CheckAsync"/>. A small,
    /// immutable summary the consumer can render as a banner or use to
    /// gate a "would you like to update?" prompt.
    /// </summary>
    /// <param name="CurrentVersion">
    /// The running CLI's version as reported by
    /// <see cref="System.Reflection.AssemblyInformationalVersionAttribute"/>,
    /// stripped of any <c>+sha</c> build metadata. Returned unchanged from
    /// the checker that produced the value.
    /// </param>
    /// <param name="LatestTag">Tag of the newest release seen on the source.</param>
    /// <param name="IsUpdateAvailable">
    /// <see langword="true"/> when <see cref="LatestTag"/> compares as
    /// strictly newer than <see cref="CurrentVersion"/>.
    /// </param>
    /// <param name="ReleaseUrl">
    /// Optional human-friendly URL pointing at the latest release page for
    /// rendering in a banner.
    /// </param>
    public sealed record UpdateInfo(
        string CurrentVersion,
        string LatestTag,
        bool IsUpdateAvailable,
        Uri? ReleaseUrl);
}
