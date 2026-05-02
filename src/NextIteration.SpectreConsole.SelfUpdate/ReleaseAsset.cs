namespace NextIteration.SpectreConsole.SelfUpdate
{
    /// <summary>
    /// A single downloadable artifact within a <see cref="RemoteRelease"/>.
    /// </summary>
    /// <param name="Name">
    /// File name as published (e.g. <c>"myapp-v1.4.2-linux-x64.tar.gz"</c>).
    /// Used by <see cref="IAssetResolver"/> to pick the right archive for the
    /// running runtime.
    /// </param>
    /// <param name="DownloadUrl">
    /// Absolute URL the source can use to fetch the asset, or
    /// <see langword="null"/> when the source does not expose a stable URL
    /// (e.g. <see cref="Sources.GhCliReleaseSource"/> shells out to
    /// <c>gh release download</c> and identifies assets by tag + name
    /// rather than by URL). Sources that do publish a URL always populate
    /// it; downstream code that needs a URL should null-check first.
    /// </param>
    /// <param name="SizeBytes">Size in bytes if the source publishes one.</param>
    /// <param name="ContentType">MIME type if the source publishes one.</param>
    /// <param name="Metadata">
    /// Source-specific opaque metadata. The package does not interpret these
    /// values — sources may use them to thread extra context through to a
    /// custom <see cref="IAssetResolver"/> or <see cref="IPackageVerifier"/>
    /// (e.g. an embedded SHA-256, a per-asset signature URL).
    /// </param>
    public sealed record ReleaseAsset(
        string Name,
        Uri? DownloadUrl,
        long? SizeBytes,
        string? ContentType,
        IReadOnlyDictionary<string, string> Metadata);
}
