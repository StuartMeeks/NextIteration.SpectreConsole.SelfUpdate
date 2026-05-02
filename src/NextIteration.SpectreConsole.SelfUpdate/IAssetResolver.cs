namespace NextIteration.SpectreConsole.SelfUpdate
{
    /// <summary>
    /// Picks a single <see cref="ReleaseAsset"/> from a release given the
    /// running runtime identifier. The default implementation matches a
    /// format-agnostic naming convention (<c>{app}-v{ver}-{rid}.zip</c> or
    /// <c>.tar.gz</c>) with a sensible fallback chain. Implement this
    /// interface to override that picker for releases that follow a
    /// different naming scheme.
    /// </summary>
    public interface IAssetResolver
    {
        /// <summary>
        /// Resolve which asset to download for the given runtime, or
        /// <see langword="null"/> when nothing matches — in which case the
        /// installer surfaces a clear "no asset matches RID X; available: …"
        /// error.
        /// </summary>
        /// <param name="release">The candidate release.</param>
        /// <param name="runtimeIdentifier">
        /// .NET runtime identifier of the running CLI (<c>win-x64</c>,
        /// <c>linux-arm64</c>, <c>osx-arm64</c>, etc.) — supply
        /// <see cref="RuntimeIdentifier.Detect"/> at the call site.
        /// </param>
        ReleaseAsset? Resolve(RemoteRelease release, string runtimeIdentifier);
    }
}
