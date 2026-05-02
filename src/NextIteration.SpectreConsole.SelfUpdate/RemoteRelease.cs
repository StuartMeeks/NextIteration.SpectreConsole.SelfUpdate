namespace NextIteration.SpectreConsole.SelfUpdate
{
    /// <summary>
    /// A release returned by an <see cref="IUpdateSource"/>. The minimum
    /// information the package needs to evaluate "is this an upgrade?" and
    /// pick a downloadable artifact.
    /// </summary>
    /// <param name="Tag">
    /// The release tag exactly as the source published it (e.g. <c>"v1.4.2"</c>
    /// or <c>"1.4.2"</c>). Comparison logic strips a leading <c>v</c>/<c>V</c>.
    /// </param>
    /// <param name="Channel">
    /// Optional channel identifier (e.g. <c>"stable"</c>, <c>"beta"</c>).
    /// <see langword="null"/> means the source's default channel.
    /// </param>
    /// <param name="ReleaseNotesUrl">Optional human-friendly release-notes URL.</param>
    /// <param name="Assets">All downloadable artifacts attached to the release.</param>
    /// <param name="PublishedAt">Wall-clock time the release was published.</param>
    public sealed record RemoteRelease(
        string Tag,
        string? Channel,
        Uri? ReleaseNotesUrl,
        IReadOnlyList<ReleaseAsset> Assets,
        DateTimeOffset PublishedAt);
}
