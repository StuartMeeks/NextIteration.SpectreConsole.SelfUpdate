using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace NextIteration.SpectreConsole.SelfUpdate.Tests
{
    public sealed class ServiceCollectionExtensionsTests
    {
        [Fact]
        public void AddSelfUpdater_when_app_name_blank_throws()
        {
            var services = new ServiceCollection();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                services.AddSelfUpdater(opts => opts.UseGitHubReleases("acme/repo")));
            Assert.Contains("AppName", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AddSelfUpdater_when_no_source_configured_throws()
        {
            var services = new ServiceCollection();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                services.AddSelfUpdater(opts => opts.AppName = "myapp"));
            Assert.Contains("update source", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void AddSelfUpdater_with_github_http_registers_full_pipeline()
        {
            var services = new ServiceCollection();
            services.AddSelfUpdater(opts =>
            {
                opts.AppName = "myapp";
                opts.UseGitHubReleases("acme/myapp");
            });

            using var sp = services.BuildServiceProvider();

            Assert.NotNull(sp.GetRequiredService<ISelfUpdater>());
            Assert.NotNull(sp.GetRequiredService<IUpdateChecker>());
            Assert.NotNull(sp.GetRequiredService<IUpdateInstaller>());
            Assert.NotNull(sp.GetRequiredService<IUpdateSource>());
            Assert.NotNull(sp.GetRequiredService<IAssetResolver>());
            // SHA-256 verifier is on by default.
            Assert.NotEmpty(sp.GetServices<IPackageVerifier>());
        }

        [Fact]
        public void AddSelfUpdater_with_gh_cli_registers_pipeline_without_http_client()
        {
            var services = new ServiceCollection();
            services.AddSelfUpdater(opts =>
            {
                opts.AppName = "myapp";
                opts.UseGitHubReleases("acme/myapp", GitHubTransport.GhCli);
            });

            using var sp = services.BuildServiceProvider();
            Assert.NotNull(sp.GetRequiredService<ISelfUpdater>());
        }

        [Fact]
        public void AddSelfUpdater_with_http_manifest_registers_pipeline()
        {
            var services = new ServiceCollection();
            services.AddSelfUpdater(opts =>
            {
                opts.AppName = "myapp";
                opts.UseHttpManifest(new Uri("https://example.com/latest.json"));
            });

            using var sp = services.BuildServiceProvider();
            Assert.NotNull(sp.GetRequiredService<ISelfUpdater>());
        }

        [Fact]
        public void AddSelfUpdater_when_default_sha256_disabled_omits_default_verifier()
        {
            var services = new ServiceCollection();
            services.AddSelfUpdater(opts =>
            {
                opts.AppName = "myapp";
                opts.UseGitHubReleases("acme/myapp");
                opts.UseDefaultSha256Verifier = false;
            });

            using var sp = services.BuildServiceProvider();
            Assert.Empty(sp.GetServices<IPackageVerifier>());
        }

        [Fact]
        public void AddSelfUpdater_with_extra_verifier_factory_registers_it()
        {
            var stubVerifier = new StubVerifier();
            var services = new ServiceCollection();
            services.AddSelfUpdater(opts =>
            {
                opts.AppName = "myapp";
                opts.UseGitHubReleases("acme/myapp");
                opts.AddVerifier(_ => stubVerifier);
                opts.UseDefaultSha256Verifier = false;
            });

            using var sp = services.BuildServiceProvider();
            var verifiers = sp.GetServices<IPackageVerifier>().ToArray();
            Assert.Single(verifiers);
            Assert.Same(stubVerifier, verifiers[0]);
        }

        [Fact]
        public void AddSelfUpdater_with_custom_source_factory_registers_it()
        {
            var stubSource = new StubSource();
            var services = new ServiceCollection();
            services.AddSelfUpdater(opts =>
            {
                opts.AppName = "myapp";
                opts.UseSource(_ => stubSource);
            });

            using var sp = services.BuildServiceProvider();
            var resolved = sp.GetRequiredService<IUpdateSource>();
            Assert.Same(stubSource, resolved);
        }

        private sealed class StubVerifier : IPackageVerifier
        {
            public Task VerifyAsync(string downloadedFilePath, RemoteRelease release, ReleaseAsset asset, CancellationToken ct) =>
                Task.CompletedTask;
        }

        private sealed class StubSource : IUpdateSource
        {
            public Task<RemoteRelease?> GetLatestAsync(string? channel, CancellationToken ct) =>
                Task.FromResult<RemoteRelease?>(null);

            public Task DownloadAssetAsync(ReleaseAsset asset, Stream destination, IProgress<DownloadProgress>? progress, CancellationToken ct) =>
                Task.CompletedTask;
        }
    }
}
