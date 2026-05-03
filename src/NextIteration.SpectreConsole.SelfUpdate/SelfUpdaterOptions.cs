namespace NextIteration.SpectreConsole.SelfUpdate
{
    /// <summary>
    /// Configuration for the self-updater, passed to
    /// <c>services.AddSelfUpdater(...)</c>. Required:
    /// <see cref="AppName"/> and exactly one source (call one of
    /// <see cref="UseGitHubReleases"/>, <see cref="UseHttpManifest"/>,
    /// <see cref="UseSource{TSource}()"/>, or <see cref="UseSource(Func{IServiceProvider, IUpdateSource})"/>).
    /// </summary>
    public sealed class SelfUpdaterOptions
    {
        /// <summary>
        /// Required. Logical CLI name. Used to compute the per-user cache
        /// directory (when <see cref="CacheDirectory"/> is null), the
        /// default opt-out env-var name (<c>&lt;APP&gt;_SKIP_UPDATE_CHECK</c>),
        /// and the asset-name prefix the default resolver looks for.
        /// </summary>
        public string AppName { get; set; } = string.Empty;

        /// <summary>
        /// Optional release channel filter, e.g. <c>"stable"</c>,
        /// <c>"beta"</c>. Forwarded to <see cref="IUpdateSource.GetLatestAsync"/>;
        /// the source decides what the value means.
        /// <see langword="null"/> uses the source's default channel.
        /// </summary>
        public string? Channel { get; set; }

        /// <summary>
        /// When <see langword="true"/>, the source is asked to consider
        /// pre-release tags when resolving "latest". Sources may ignore
        /// this if pre-releases aren't a concept on their backend.
        /// Defaults to <see langword="false"/>.
        /// </summary>
        public bool IncludePrereleases { get; set; }

        /// <summary>
        /// How long the cached "latest release" answer stays fresh on disk
        /// before the next call hits the source again. Defaults to 24 hours
        /// — matches the pl-app reference implementation.
        /// </summary>
        public TimeSpan CacheTtl { get; set; } = TimeSpan.FromHours(24);

        /// <summary>
        /// Maximum time the background check is allowed to spend talking to
        /// the source. Defaults to 3 seconds — long enough for a healthy
        /// network, short enough to never delay a CLI invocation.
        /// </summary>
        public TimeSpan CheckTimeout { get; set; } = TimeSpan.FromSeconds(3);

        /// <summary>
        /// Maximum time an asset download is allowed to take before the
        /// installer aborts. Defaults to 5 minutes.
        /// </summary>
        public TimeSpan DownloadTimeout { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Override the default cache directory. <see langword="null"/>
        /// (default) resolves to
        /// <c>%APPDATA%/&lt;AppName&gt;/</c> on Windows,
        /// <c>$XDG_CACHE_HOME/&lt;AppName&gt;/</c> (or
        /// <c>~/.cache/&lt;AppName&gt;/</c>) on Linux, and
        /// <c>~/Library/Caches/&lt;AppName&gt;/</c> on macOS.
        /// </summary>
        public string? CacheDirectory { get; set; }

        /// <summary>
        /// Name of the env var that, when set to <c>"1"</c>, suppresses the
        /// background check (useful in CI / scripted callers).
        /// <see langword="null"/> (default) computes
        /// <c>&lt;APPNAME&gt;_SKIP_UPDATE_CHECK</c> with <see cref="AppName"/>
        /// upper-cased and non-alphanumeric characters replaced with <c>_</c>.
        /// </summary>
        public string? SkipCheckEnvironmentVariable { get; set; }

        /// <summary>
        /// Predicate evaluated against the running CLI's resolved version
        /// string. Returning <see langword="true"/> suppresses the check —
        /// useful for treating a default <c>"1.0.0"</c> placeholder version
        /// (i.e. an unstamped local <c>dotnet run</c>) as "dev build, don't
        /// bother checking". Defaults to a predicate that returns
        /// <see langword="true"/> for the literal string <c>"1.0.0"</c>,
        /// matching the pl-app reference.
        /// </summary>
        public Func<string, bool>? SkipVersionPredicate { get; set; } =
            v => string.Equals(v, "1.0.0", StringComparison.Ordinal);

        /// <summary>
        /// Optional GitHub personal access token used by
        /// <c>HttpGitHubReleaseSource</c> for higher rate limits or access
        /// to private repositories. When <see langword="null"/> the source
        /// falls back to the <c>GITHUB_TOKEN</c> environment variable; if
        /// neither is set the source makes anonymous requests.
        /// </summary>
        public string? GitHubToken { get; set; }

        /// <summary>
        /// When <see langword="true"/> (default) the SHA-256 verifier is
        /// registered alongside any custom verifiers added via
        /// <c>AddVerifier</c>. Set to <see langword="false"/> for sources
        /// that don't ship a <c>SHA256SUMS.txt</c> manifest — but consider
        /// adding an alternative verifier (signature checking) before doing
        /// so, since downloading without verification is unsafe.
        /// </summary>
        public bool UseDefaultSha256Verifier { get; set; } = true;

        /// <summary>
        /// When <see langword="false"/> (default) the
        /// <see cref="Sources.HttpManifestSource"/> rejects non-<c>https</c>
        /// manifest URLs and non-<c>https</c> asset download URLs. Set to
        /// <see langword="true"/> to allow plain HTTP — useful for tests,
        /// internal mirrors on a trusted network, and local development.
        /// Production deployments should always leave this as
        /// <see langword="false"/>: the SHA in an HTTP-served manifest is
        /// equally MITM-able, so plain HTTP defeats the verifier.
        /// </summary>
        public bool AllowInsecureManifestSource { get; set; }

        // ---------- Source registration ----------

        /// <summary>
        /// Use the GitHub Releases source backed by either HttpClient
        /// (default; works for public repos) or the <c>gh</c> CLI (for
        /// private repos).
        /// </summary>
        /// <param name="repository">
        /// The GitHub repository slug, e.g. <c>"acme/my-cli"</c>.
        /// </param>
        /// <param name="transport">Transport selector.</param>
        public void UseGitHubReleases(string repository, GitHubTransport transport = GitHubTransport.HttpClient)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(repository);
            SourceKind = UpdateSourceKind.GitHub;
            GitHubRepository = repository;
            GitHubTransport = transport;
            ManifestUrl = null;
            CustomSourceType = null;
            CustomSourceFactory = null;
        }

        /// <summary>
        /// Use the generic HTTPS-manifest source. The configured URL must
        /// return a JSON document of the shape:
        /// <c>{ "tag": "v1.4.2", "channel": "stable", "publishedAt":
        /// "2026-04-30T12:00:00Z", "releaseNotesUrl": "...", "assets":
        /// [{ "name": "...", "url": "...", "sizeBytes": 12345,
        /// "contentType": "...", "sha256": "..." }, ...] }</c>.
        /// </summary>
        public void UseHttpManifest(Uri manifestUrl)
        {
            ArgumentNullException.ThrowIfNull(manifestUrl);
            SourceKind = UpdateSourceKind.HttpManifest;
            ManifestUrl = manifestUrl;
            GitHubRepository = null;
            CustomSourceType = null;
            CustomSourceFactory = null;
        }

        /// <summary>
        /// Use a custom <see cref="IUpdateSource"/> implementation, resolved
        /// from DI. The type is registered as a singleton.
        /// </summary>
        public void UseSource<TSource>()
            where TSource : class, IUpdateSource
        {
            SourceKind = UpdateSourceKind.CustomType;
            CustomSourceType = typeof(TSource);
            CustomSourceFactory = null;
            GitHubRepository = null;
            ManifestUrl = null;
        }

        /// <summary>
        /// Use a custom <see cref="IUpdateSource"/> built by the supplied
        /// factory. Useful when the source needs constructor parameters
        /// resolved from the same scope as the rest of the application.
        /// </summary>
        public void UseSource(Func<IServiceProvider, IUpdateSource> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            SourceKind = UpdateSourceKind.CustomFactory;
            CustomSourceFactory = factory;
            CustomSourceType = null;
            GitHubRepository = null;
            ManifestUrl = null;
        }

        // ---------- Asset resolver override ----------

        /// <summary>
        /// Replace the default <see cref="IAssetResolver"/> with the named
        /// type, resolved from DI as a singleton.
        /// </summary>
        public void UseAssetResolver<TResolver>()
            where TResolver : class, IAssetResolver
        {
            AssetResolverType = typeof(TResolver);
            AssetResolverFactory = null;
            AssetResolverFunc = null;
        }

        /// <summary>
        /// Replace the default <see cref="IAssetResolver"/> with one built
        /// by the supplied factory.
        /// </summary>
        public void UseAssetResolver(Func<IServiceProvider, IAssetResolver> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            AssetResolverFactory = factory;
            AssetResolverType = null;
            AssetResolverFunc = null;
        }

        /// <summary>
        /// Replace the default <see cref="IAssetResolver"/> with a plain
        /// delegate. Convenience wrapper for one-line overrides; the
        /// delegate is wrapped in an internal adapter and registered as a
        /// singleton.
        /// </summary>
        public void UseAssetResolver(Func<RemoteRelease, string, ReleaseAsset?> resolver)
        {
            ArgumentNullException.ThrowIfNull(resolver);
            AssetResolverFunc = resolver;
            AssetResolverType = null;
            AssetResolverFactory = null;
        }

        // ---------- Additional verifiers ----------

        /// <summary>
        /// Add an additional <see cref="IPackageVerifier"/> to the pipeline,
        /// resolved from DI as a singleton. Combine with
        /// <see cref="UseDefaultSha256Verifier"/> = false to opt out of the
        /// built-in SHA-256 check entirely.
        /// </summary>
        public void AddVerifier<TVerifier>()
            where TVerifier : class, IPackageVerifier
        {
            ExtraVerifierTypes.Add(typeof(TVerifier));
        }

        /// <summary>
        /// Add an additional <see cref="IPackageVerifier"/> built by the
        /// supplied factory.
        /// </summary>
        public void AddVerifier(Func<IServiceProvider, IPackageVerifier> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            ExtraVerifierFactories.Add(factory);
        }

        // ---------- Internal state read by ServiceCollectionExtensions ----------

        internal UpdateSourceKind SourceKind { get; private set; }

        internal string? GitHubRepository { get; private set; }
        internal GitHubTransport GitHubTransport { get; private set; }

        internal Uri? ManifestUrl { get; private set; }

        internal Type? CustomSourceType { get; private set; }
        internal Func<IServiceProvider, IUpdateSource>? CustomSourceFactory { get; private set; }

        internal Type? AssetResolverType { get; private set; }
        internal Func<IServiceProvider, IAssetResolver>? AssetResolverFactory { get; private set; }
        internal Func<RemoteRelease, string, ReleaseAsset?>? AssetResolverFunc { get; private set; }

        internal List<Type> ExtraVerifierTypes { get; } = new();
        internal List<Func<IServiceProvider, IPackageVerifier>> ExtraVerifierFactories { get; } = new();
    }

    internal enum UpdateSourceKind
    {
        Unset = 0,
        GitHub,
        HttpManifest,
        CustomType,
        CustomFactory,
    }
}
