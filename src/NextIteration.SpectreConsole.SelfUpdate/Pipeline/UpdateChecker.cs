using System.Reflection;
using System.Text;

namespace NextIteration.SpectreConsole.SelfUpdate.Pipeline
{
    /// <summary>
    /// Default <see cref="IUpdateChecker"/>. Composes the configured
    /// <see cref="IUpdateSource"/> with a per-user JSON cache file and
    /// honours the opt-out env var + skip-version predicate from
    /// <see cref="SelfUpdaterOptions"/>.
    /// </summary>
    internal sealed class UpdateChecker : IUpdateChecker
    {
        private readonly SelfUpdaterOptions _options;
        private readonly IUpdateSource _source;
        private readonly Func<string?> _currentVersionResolver;
        private readonly Func<string, string?> _envResolver;
        private readonly Func<DateTimeOffset> _utcNow;

        public UpdateChecker(SelfUpdaterOptions options, IUpdateSource source)
            : this(options, source,
                  currentVersionResolver: ResolveEntryAssemblyVersion,
                  envResolver: Environment.GetEnvironmentVariable,
                  utcNow: () => DateTimeOffset.UtcNow)
        {
        }

        internal UpdateChecker(
            SelfUpdaterOptions options,
            IUpdateSource source,
            Func<string?> currentVersionResolver,
            Func<string, string?> envResolver,
            Func<DateTimeOffset> utcNow)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(currentVersionResolver);
            ArgumentNullException.ThrowIfNull(envResolver);
            ArgumentNullException.ThrowIfNull(utcNow);

            _options = options;
            _source = source;
            _currentVersionResolver = currentVersionResolver;
            _envResolver = envResolver;
            _utcNow = utcNow;
        }

        public Task<UpdateInfo?> CheckAsync(CancellationToken ct = default) =>
            CheckAsync(includePrereleasesOverride: null, ct);

        public async Task<UpdateInfo?> CheckAsync(bool? includePrereleasesOverride, CancellationToken ct = default)
        {
            if (IsOptOutSet()) return null;

            var current = GetCurrentVersion();
            if (current is null) return null;
            if (IsVersionSkipped(current)) return null;

            var effectivePrerelease = includePrereleasesOverride ?? _options.IncludePrereleases;
            var cachePath = ResolveCacheFilePath();
            var cached = UpdateCacheFile.TryRead(cachePath);
            if (IsCacheFresh(cached, effectivePrerelease))
            {
                return Compare(current, cached!.LatestTag, ParseUri(cached.ReleaseUrl));
            }

            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linked.CancelAfter(_options.CheckTimeout);

                var release = await _source.GetLatestAsync(_options.Channel, includePrereleasesOverride, linked.Token).ConfigureAwait(false);
                if (release is null) return null;

                UpdateCacheFile.TryWrite(cachePath, new UpdateCacheEntry(
                    CheckedAt: _utcNow(),
                    LatestTag: release.Tag,
                    ReleaseUrl: release.ReleaseNotesUrl?.ToString(),
                    Channel: _options.Channel,
                    IncludePrereleases: effectivePrerelease));

                return Compare(current, release.Tag, release.ReleaseNotesUrl);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public string? GetCurrentVersion()
        {
            var v = _currentVersionResolver();
            if (string.IsNullOrWhiteSpace(v)) return null;
            return StripBuildMetadata(v);
        }

        internal bool IsVersionSkipped(string version) =>
            _options.SkipVersionPredicate is { } skip && skip(version);

        // ---------- Internals (testable via InternalsVisibleTo) ----------

        internal bool IsOptOutSet()
        {
            var name = ResolveSkipEnvVarName();
            if (string.IsNullOrWhiteSpace(name)) return false;
            return string.Equals(_envResolver(name), "1", StringComparison.Ordinal);
        }

        internal string ResolveSkipEnvVarName()
        {
            if (!string.IsNullOrWhiteSpace(_options.SkipCheckEnvironmentVariable))
            {
                return _options.SkipCheckEnvironmentVariable!;
            }
            return ComputeDefaultSkipEnvVarName(_options.AppName);
        }

        internal string ResolveCacheFilePath()
        {
            var dir = !string.IsNullOrWhiteSpace(_options.CacheDirectory)
                ? _options.CacheDirectory!
                : DefaultCacheDirectory(_options.AppName);
            return Path.Combine(dir, ".update-check.json");
        }

        internal bool IsCacheFresh(UpdateCacheEntry? entry) =>
            IsCacheFresh(entry, _options.IncludePrereleases);

        internal bool IsCacheFresh(UpdateCacheEntry? entry, bool effectivePrerelease)
        {
            if (entry is null) return false;
            if (!string.Equals(entry.Channel, _options.Channel, StringComparison.Ordinal)) return false;
            // Pre-0.1.4 cache entries have no IncludePrereleases field — they
            // were written before the override existed, so they always
            // reflect a non-prerelease answer.
            if ((entry.IncludePrereleases ?? false) != effectivePrerelease) return false;
            return (_utcNow() - entry.CheckedAt) < _options.CacheTtl;
        }

        internal static UpdateInfo Compare(string current, string latestTag, Uri? releaseUrl)
        {
            var available = IsNewer(current, latestTag);
            return new UpdateInfo(current, latestTag, available, releaseUrl);
        }

        internal static bool IsNewer(string current, string latestTag)
        {
            var c = StripLeadingV(current);
            var l = StripLeadingV(latestTag);
            var cNumeric = SplitNumericPrerelease(c).Numeric;
            var lNumeric = SplitNumericPrerelease(l).Numeric;

            if (!Version.TryParse(cNumeric, out var cv) || !Version.TryParse(lNumeric, out var lv))
            {
                return false;
            }

            if (lv > cv) return true;
            if (lv < cv) return false;

            // Numeric versions equal — compare prereleases. Semver: a release
            // without a prerelease is newer than one with a prerelease at the
            // same numeric version.
            var cPre = SplitNumericPrerelease(c).Prerelease;
            var lPre = SplitNumericPrerelease(l).Prerelease;
            return ComparePrerelease(cPre, lPre) < 0;
        }

        internal static int ComparePrerelease(string? current, string? latest)
        {
            if (current is null && latest is null) return 0;
            if (current is null) return 1;   // no-prerelease > prerelease, so current is newer
            if (latest is null) return -1;   // current has prerelease, latest does not → current is older
            return string.CompareOrdinal(current, latest);
        }

        internal static (string Numeric, string? Prerelease) SplitNumericPrerelease(string version)
        {
            var dash = version.IndexOf('-', StringComparison.Ordinal);
            if (dash < 0) return (version, null);
            return (version[..dash], version[(dash + 1)..]);
        }

        internal static string StripLeadingV(string version)
        {
            if (string.IsNullOrEmpty(version)) return string.Empty;
            return version[0] is 'v' or 'V' ? version[1..] : version;
        }

        internal static string StripBuildMetadata(string version)
        {
            var plus = version.IndexOf('+', StringComparison.Ordinal);
            return plus < 0 ? version : version[..plus];
        }

        internal static string ComputeDefaultSkipEnvVarName(string appName)
        {
            if (string.IsNullOrWhiteSpace(appName)) return string.Empty;
            var sb = new StringBuilder(appName.Length + "_SKIP_UPDATE_CHECK".Length);
            foreach (var c in appName)
            {
                sb.Append(char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_');
            }
            sb.Append("_SKIP_UPDATE_CHECK");
            return sb.ToString();
        }

        internal static string DefaultCacheDirectory(string appName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(appName);
            string root;
            if (OperatingSystem.IsWindows())
            {
                root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }
            else if (OperatingSystem.IsMacOS())
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                root = Path.Combine(home, "Library", "Caches");
            }
            else
            {
                var xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
                if (string.IsNullOrWhiteSpace(xdg))
                {
                    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    xdg = Path.Combine(home, ".cache");
                }
                root = xdg;
            }
            return Path.Combine(root, appName);
        }

        // ---------- Helpers ----------

        private static Uri? ParseUri(string? value) =>
            !string.IsNullOrWhiteSpace(value) && Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;

        private static string? ResolveEntryAssemblyVersion()
        {
            var asm = Assembly.GetEntryAssembly();
            var attr = asm?.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            return attr?.InformationalVersion;
        }
    }
}
