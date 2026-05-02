using Microsoft.Extensions.DependencyInjection;

using NextIteration.SpectreConsole.SelfUpdate.Commands;
using NextIteration.SpectreConsole.SelfUpdate.Pipeline;
using NextIteration.SpectreConsole.SelfUpdate.Resolution;
using NextIteration.SpectreConsole.SelfUpdate.Sources;
using NextIteration.SpectreConsole.SelfUpdate.Verification;

namespace NextIteration.SpectreConsole.SelfUpdate
{
    /// <summary>
    /// DI extensions for registering the
    /// <c>NextIteration.SpectreConsole.SelfUpdate</c> services.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers <see cref="ISelfUpdater"/> and its dependencies — the
        /// configured <see cref="IUpdateSource"/>, the
        /// <see cref="IAssetResolver"/> (default if not overridden), every
        /// configured <see cref="IPackageVerifier"/> (the SHA-256 default
        /// is included unless
        /// <see cref="SelfUpdaterOptions.UseDefaultSha256Verifier"/> is set
        /// to <see langword="false"/>), the cache-aware
        /// <see cref="IUpdateChecker"/> and the file-swap
        /// <see cref="IUpdateInstaller"/>. The HTTP-backed sources also
        /// trigger a call to <c>AddHttpClient()</c> — safe to combine with
        /// any prior registration.
        /// </summary>
        public static IServiceCollection AddSelfUpdater(
            this IServiceCollection services,
            Action<SelfUpdaterOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);

            var options = new SelfUpdaterOptions();
            configure(options);

            ValidateOptions(options);

            services.AddSingleton(options);

            if (RequiresHttpClient(options))
            {
                services.AddHttpClient();
            }

            RegisterSource(services, options);
            RegisterAssetResolver(services, options);
            RegisterVerifiers(services, options);

            services.AddSingleton<IUpdateChecker, UpdateChecker>();
            services.AddSingleton<IUpdateInstaller, UpdateInstaller>();
            services.AddSingleton<ISelfUpdater, SelfUpdater>();

            // Pre-register the Spectre commands so a TypeRegistrar that
            // wraps a pre-built ServiceProvider can resolve them without
            // additional consumer wiring.
            services.AddSingleton<UpdateCommand>();
            services.AddSingleton<UpdateCheckCommand>();

            return services;
        }

        private static void ValidateOptions(SelfUpdaterOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.AppName))
            {
                throw new InvalidOperationException(
                    $"{nameof(SelfUpdaterOptions)}.{nameof(SelfUpdaterOptions.AppName)} must be set before calling {nameof(AddSelfUpdater)}.");
            }
            if (options.SourceKind == UpdateSourceKind.Unset)
            {
                throw new InvalidOperationException(
                    "An update source must be configured. Call one of UseGitHubReleases, UseHttpManifest, UseSource<T>, or UseSource(factory) on the options.");
            }
        }

        private static bool RequiresHttpClient(SelfUpdaterOptions options) =>
            options.SourceKind == UpdateSourceKind.HttpManifest
            || (options.SourceKind == UpdateSourceKind.GitHub
                && options.GitHubTransport == GitHubTransport.HttpClient);

        private static void RegisterSource(IServiceCollection services, SelfUpdaterOptions options)
        {
            switch (options.SourceKind)
            {
                case UpdateSourceKind.GitHub when options.GitHubTransport == GitHubTransport.HttpClient:
                    services.AddSingleton<IUpdateSource>(sp => new HttpGitHubReleaseSource(
                        sp.GetRequiredService<IHttpClientFactory>(),
                        options.GitHubRepository!,
                        options.GitHubToken,
                        options.IncludePrereleases));
                    break;

                case UpdateSourceKind.GitHub when options.GitHubTransport == GitHubTransport.GhCli:
                    services.AddSingleton<IUpdateSource>(_ => new GhCliReleaseSource(
                        options.GitHubRepository!,
                        options.IncludePrereleases));
                    break;

                case UpdateSourceKind.HttpManifest:
                    services.AddSingleton<IUpdateSource>(sp => new HttpManifestSource(
                        sp.GetRequiredService<IHttpClientFactory>(),
                        options.ManifestUrl!));
                    break;

                case UpdateSourceKind.CustomType:
                    services.AddSingleton(typeof(IUpdateSource), options.CustomSourceType!);
                    break;

                case UpdateSourceKind.CustomFactory:
                    services.AddSingleton<IUpdateSource>(options.CustomSourceFactory!);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported update source configuration: {options.SourceKind}.");
            }
        }

        private static void RegisterAssetResolver(IServiceCollection services, SelfUpdaterOptions options)
        {
            if (options.AssetResolverType is not null)
            {
                services.AddSingleton(typeof(IAssetResolver), options.AssetResolverType);
            }
            else if (options.AssetResolverFactory is not null)
            {
                services.AddSingleton<IAssetResolver>(options.AssetResolverFactory);
            }
            else if (options.AssetResolverFunc is not null)
            {
                services.AddSingleton<IAssetResolver>(_ => new FuncAssetResolver(options.AssetResolverFunc));
            }
            else
            {
                services.AddSingleton<IAssetResolver>(_ => new DefaultAssetResolver(options.AppName));
            }
        }

        private static void RegisterVerifiers(IServiceCollection services, SelfUpdaterOptions options)
        {
            if (options.UseDefaultSha256Verifier)
            {
                services.AddSingleton<IPackageVerifier, Sha256ChecksumVerifier>();
            }
            foreach (var t in options.ExtraVerifierTypes)
            {
                services.AddSingleton(typeof(IPackageVerifier), t);
            }
            foreach (var f in options.ExtraVerifierFactories)
            {
                services.AddSingleton<IPackageVerifier>(f);
            }
        }
    }
}
