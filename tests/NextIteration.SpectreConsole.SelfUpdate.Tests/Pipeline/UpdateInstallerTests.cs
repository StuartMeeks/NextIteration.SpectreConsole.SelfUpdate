using System.IO.Compression;

using NextIteration.SpectreConsole.SelfUpdate.Pipeline;
using NextIteration.SpectreConsole.SelfUpdate.Resolution;
using NextIteration.SpectreConsole.SelfUpdate.Tests.Infrastructure;

using Xunit;

namespace NextIteration.SpectreConsole.SelfUpdate.Tests.Pipeline
{
    public sealed class UpdateInstallerTests
    {
        [Fact]
        public async Task InstallAsync_swaps_files_into_install_directory()
        {
            using var work = new TempDir();
            var installDir = Path.Combine(work.Path, "install");
            Directory.CreateDirectory(installDir);
            File.WriteAllText(Path.Combine(installDir, "old-file.txt"), "old content");

            var release = BuildReleaseWithSingleAssetZip(
                tag: "v1.4.2",
                assetName: "myapp-v1.4.2-linux-x64.zip",
                inner: ("myapp.exe", "new binary"),
                inner2: ("settings.json", "{}"));

            var source = new FakeUpdateSource
            {
                AssetBytes = a => CreateZipBytes(("myapp-v1.4.2-linux-x64", new[] {
                    ("myapp.exe", "new binary"),
                    ("settings.json", "{}"),
                })),
            };

            var installer = NewInstaller(installDir, source, "linux-x64");
            await installer.InstallAsync(release, progress: null, CancellationToken.None);

            // New files in place
            Assert.Equal("new binary", await File.ReadAllTextAsync(Path.Combine(installDir, "myapp.exe")));
            Assert.Equal("{}", await File.ReadAllTextAsync(Path.Combine(installDir, "settings.json")));

            // Old file moved to .old/
            Assert.True(Directory.Exists(Path.Combine(installDir, ".old")));
            Assert.Equal("old content", await File.ReadAllTextAsync(Path.Combine(installDir, ".old", "old-file.txt")));

            // Staging removed
            Assert.False(Directory.Exists(Path.Combine(installDir, ".update")));
        }

        [Fact]
        public async Task InstallAsync_throws_when_no_asset_matches_rid()
        {
            using var work = new TempDir();
            var installDir = Path.Combine(work.Path, "install");
            Directory.CreateDirectory(installDir);

            var release = new RemoteRelease(
                Tag: "v1.0.0",
                Channel: null,
                ReleaseNotesUrl: null,
                Assets: new[]
                {
                    new ReleaseAsset("myapp-v1.0.0-osx-arm64.zip", new Uri("https://example.com/x"), 100, null, new Dictionary<string, string>()),
                },
                PublishedAt: DateTimeOffset.UtcNow);

            var installer = NewInstaller(installDir, new FakeUpdateSource(), "linux-arm64");

            var ex = await Assert.ThrowsAsync<UpdateException>(() =>
                installer.InstallAsync(release, progress: null, CancellationToken.None));

            Assert.Contains("No release asset matches RID 'linux-arm64'", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task InstallAsync_reports_progress_for_each_stage()
        {
            using var work = new TempDir();
            var installDir = Path.Combine(work.Path, "install");
            Directory.CreateDirectory(installDir);

            var release = BuildReleaseWithSingleAssetZip("v1.0.0", "myapp-v1.0.0-linux-x64.zip", ("a.txt", "x"), ("b.txt", "y"));
            var source = new FakeUpdateSource
            {
                AssetBytes = a => CreateZipBytes(("myapp-v1.0.0-linux-x64", new[] { ("a.txt", "x"), ("b.txt", "y") })),
            };

            var stages = new List<UpdateStage>();
            var progress = new Progress<UpdateProgressEvent>(e => stages.Add(e.Stage));
            var installer = NewInstaller(installDir, source, "linux-x64");
            await installer.InstallAsync(release, progress, CancellationToken.None);

            // Allow Progress<T> to drain.
            await Task.Yield();

            Assert.Contains(UpdateStage.Downloading, stages);
            Assert.Contains(UpdateStage.Verifying, stages);
            Assert.Contains(UpdateStage.Extracting, stages);
            Assert.Contains(UpdateStage.Swapping, stages);
        }

        [Fact]
        public void CleanupOldInstall_when_old_directory_exists_deletes_it()
        {
            using var work = new TempDir();
            var installDir = Path.Combine(work.Path, "install");
            Directory.CreateDirectory(Path.Combine(installDir, ".old"));
            File.WriteAllText(Path.Combine(installDir, ".old", "garbage.txt"), "stale");

            var installer = NewInstaller(installDir, new FakeUpdateSource(), "linux-x64");
            installer.CleanupOldInstall();

            Assert.False(Directory.Exists(Path.Combine(installDir, ".old")));
        }

        [Fact]
        public void CleanupOldInstall_when_old_directory_missing_is_noop()
        {
            using var work = new TempDir();
            var installDir = Path.Combine(work.Path, "install");
            Directory.CreateDirectory(installDir);

            var installer = NewInstaller(installDir, new FakeUpdateSource(), "linux-x64");
            installer.CleanupOldInstall();   // does not throw
        }

        [Fact]
        public async Task InstallAsync_when_lock_file_held_throws()
        {
            using var work = new TempDir();
            var installDir = Path.Combine(work.Path, "install");
            Directory.CreateDirectory(installDir);

            var release = BuildReleaseWithSingleAssetZip("v1.0.0", "myapp-v1.0.0-linux-x64.zip", ("a.txt", "x"));
            var source = new FakeUpdateSource
            {
                AssetBytes = _ => CreateZipBytes(("myapp-v1.0.0-linux-x64", new[] { ("a.txt", "x") })),
            };

            // Hold the lock externally.
            var lockPath = Path.Combine(installDir, ".update.lock");
            using var foreignLock = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);

            var installer = NewInstaller(installDir, source, "linux-x64");
            await Assert.ThrowsAsync<UpdateException>(() =>
                installer.InstallAsync(release, progress: null, CancellationToken.None));
        }

        [Fact]
        public void Swap_preserves_maintenance_entries()
        {
            using var work = new TempDir();
            var installDir = work.Combine("install");
            Directory.CreateDirectory(installDir);
            Directory.CreateDirectory(Path.Combine(installDir, ".update", "v1"));
            Directory.CreateDirectory(Path.Combine(installDir, ".old"));
            File.WriteAllText(Path.Combine(installDir, ".update.lock"), "");
            File.WriteAllText(Path.Combine(installDir, "real-file.txt"), "live");

            var sourceDir = work.Combine("src");
            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(Path.Combine(sourceDir, "real-file.txt"), "updated");

            var oldDir = Path.Combine(installDir, ".old");
            UpdateInstaller.Swap(sourceDir, installDir, oldDir);

            Assert.Equal("updated", File.ReadAllText(Path.Combine(installDir, "real-file.txt")));
            Assert.True(Directory.Exists(Path.Combine(installDir, ".update", "v1")), ".update should not be moved into .old");
            Assert.True(File.Exists(Path.Combine(installDir, ".update.lock")), "lock file should survive the swap");
        }

        // ---------- Helpers ----------

        private static UpdateInstaller NewInstaller(string installDir, FakeUpdateSource source, string rid)
        {
            var options = new SelfUpdaterOptions
            {
                AppName = "myapp",
                UseDefaultSha256Verifier = false,   // tests run without a hash manifest
            };
            return new UpdateInstaller(
                options,
                source,
                new DefaultAssetResolver("myapp"),
                Array.Empty<IPackageVerifier>(),
                ridResolver: () => rid,
                installDirResolver: () => installDir);
        }

        private static RemoteRelease BuildReleaseWithSingleAssetZip(string tag, string assetName, params (string Name, string Content)[] inner)
        {
            return BuildReleaseWithSingleAssetZip(tag, assetName, inner.FirstOrDefault(), inner.Skip(1).FirstOrDefault());
        }

        private static RemoteRelease BuildReleaseWithSingleAssetZip(
            string tag, string assetName, (string Name, string Content) inner, (string Name, string Content) inner2)
        {
            var asset = new ReleaseAsset(
                Name: assetName,
                DownloadUrl: new Uri("https://example.com/" + assetName),
                SizeBytes: null,
                ContentType: "application/zip",
                Metadata: new Dictionary<string, string>());
            return new RemoteRelease(
                Tag: tag,
                Channel: null,
                ReleaseNotesUrl: null,
                Assets: new[] { asset },
                PublishedAt: DateTimeOffset.UtcNow);
        }

        private static byte[] CreateZipBytes((string TopFolder, (string Name, string Content)[] Files) layout)
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (name, content) in layout.Files)
                {
                    var entry = zip.CreateEntry(layout.TopFolder + "/" + name);
                    using var writer = new StreamWriter(entry.Open());
                    writer.Write(content);
                }
            }
            return ms.ToArray();
        }
    }
}
