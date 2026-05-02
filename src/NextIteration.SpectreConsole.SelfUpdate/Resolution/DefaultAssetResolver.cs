namespace NextIteration.SpectreConsole.SelfUpdate.Resolution
{
    /// <summary>
    /// Default <see cref="IAssetResolver"/>. Picks an archive whose filename
    /// follows the convention <c>{appName}-[v]{version}-{rid}.(zip|tar.gz|tgz)</c>,
    /// with progressive fallbacks for releases that drop the version, drop
    /// the app prefix, or use a different RID variant. Matches are
    /// case-insensitive.
    /// </summary>
    /// <remarks>
    /// Fallback order, first match wins:
    /// <list type="number">
    ///   <item><description><c>{appName}-v{version}-{rid}.(zip|tar.gz|tgz)</c></description></item>
    ///   <item><description><c>{appName}-{version}-{rid}.(zip|tar.gz|tgz)</c></description></item>
    ///   <item><description><c>{appName}-{rid}.(zip|tar.gz|tgz)</c></description></item>
    ///   <item><description>any asset whose filename starts with <c>{appName}</c> and ends with <c>-{rid}.(zip|tar.gz|tgz)</c></description></item>
    ///   <item><description>any asset whose filename ends with <c>-{rid}.(zip|tar.gz|tgz)</c></description></item>
    ///   <item><description>for <c>osx-arm64</c>: retry the chain with <c>osx-x64</c> (Rosetta) and bare <c>osx</c> (universal)</description></item>
    /// </list>
    /// Returns <see langword="null"/> when nothing matches; the installer
    /// surfaces the available asset names in its error message so consumers
    /// can see why detection failed.
    /// </remarks>
    public sealed class DefaultAssetResolver : IAssetResolver
    {
        private static readonly string[] ArchiveExtensions = { ".tar.gz", ".tgz", ".zip" };

        private readonly string _appName;

        /// <summary>
        /// Initializes a new instance scoped to the given CLI app name —
        /// the prefix the resolver will look for in candidate asset names.
        /// </summary>
        public DefaultAssetResolver(string appName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(appName);
            _appName = appName;
        }

        /// <inheritdoc />
        public ReleaseAsset? Resolve(RemoteRelease release, string runtimeIdentifier)
        {
            ArgumentNullException.ThrowIfNull(release);
            ArgumentException.ThrowIfNullOrWhiteSpace(runtimeIdentifier);

            foreach (var rid in CandidateRids(runtimeIdentifier))
            {
                var match = ResolveForRid(release, rid);
                if (match is not null) return match;
            }
            return null;
        }

        private ReleaseAsset? ResolveForRid(RemoteRelease release, string rid)
        {
            var versionNumeric = StripLeadingV(release.Tag);
            var versionWithV = "v" + versionNumeric;

            // 1. {app}-v{ver}-{rid}.ext
            var match = MatchExact(release, $"{_appName}-{versionWithV}-{rid}");
            if (match is not null) return match;

            // 2. {app}-{ver}-{rid}.ext (in case tag is unprefixed)
            match = MatchExact(release, $"{_appName}-{versionNumeric}-{rid}");
            if (match is not null) return match;

            // 3. {app}-{rid}.ext (no version segment)
            match = MatchExact(release, $"{_appName}-{rid}");
            if (match is not null) return match;

            // 4. {app}…-{rid}.ext (loose: starts with app, ends with -rid+ext)
            match = MatchPrefixSuffix(release, $"{_appName}-", $"-{rid}");
            if (match is not null) return match;

            // 5. *…-{rid}.ext (RID-only — last resort, may be ambiguous)
            match = MatchSuffixOnly(release, $"-{rid}");
            return match;
        }

        private static IEnumerable<string> CandidateRids(string runtimeIdentifier)
        {
            yield return runtimeIdentifier;

            // macOS fallbacks — Apple Silicon devices can run osx-x64 binaries
            // through Rosetta, and some publishers ship a single "osx" universal
            // archive instead of two arch-specific ones.
            if (string.Equals(runtimeIdentifier, "osx-arm64", StringComparison.OrdinalIgnoreCase))
            {
                yield return "osx-x64";
                yield return "osx";
            }
        }

        private static ReleaseAsset? MatchExact(RemoteRelease release, string stem)
        {
            foreach (var asset in release.Assets)
            {
                foreach (var ext in ArchiveExtensions)
                {
                    if (NameEquals(asset.Name, stem + ext)) return asset;
                }
            }
            return null;
        }

        private static ReleaseAsset? MatchPrefixSuffix(RemoteRelease release, string prefix, string ridSuffix)
        {
            foreach (var asset in release.Assets)
            {
                if (!asset.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (!EndsWithRidAndArchive(asset.Name, ridSuffix)) continue;
                return asset;
            }
            return null;
        }

        private static ReleaseAsset? MatchSuffixOnly(RemoteRelease release, string ridSuffix)
        {
            foreach (var asset in release.Assets)
            {
                if (EndsWithRidAndArchive(asset.Name, ridSuffix)) return asset;
            }
            return null;
        }

        private static bool EndsWithRidAndArchive(string name, string ridSuffix)
        {
            foreach (var ext in ArchiveExtensions)
            {
                if (name.EndsWith(ridSuffix + ext, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool NameEquals(string actual, string expected) =>
            string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

        private static string StripLeadingV(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return string.Empty;
            return tag[0] is 'v' or 'V' ? tag[1..] : tag;
        }
    }
}
