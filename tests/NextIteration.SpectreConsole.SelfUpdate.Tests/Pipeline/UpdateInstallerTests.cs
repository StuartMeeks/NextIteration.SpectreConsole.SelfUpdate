using System.IO.Compression;

using NextIteration.SpectreConsole.SelfUpdate.Pipeline;
using NextIteration.SpectreConsole.SelfUpdate.Resolution;
using NextIteration.SpectreConsole.SelfUpdate.Tests.Infrastructure;

using Xunit;

namespace NextIteration.SpectreConsole.SelfUpdate.Tests.Pipeline
{
    public sealed class UpdateInstallerTests
    {
        private static readonly string[] TwoEntryNames = { "a.txt", "subdir" };
        private static readonly string[] OneEntryName = { "a.txt" };
        private static readonly string[] PreserveAppsettingsDevelopment = { "appsettings.Development.json" };
        private static readonly string[] PreserveAppsettingsJson = { "appsettings.json" };
        private static readonly string[] PreserveDb = { "*.db" };
        private static readonly string[] PreserveAppsettingsGlob = { "appsettings.*.json" };

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
            await installer.InstallAsync(release, progress: null, onConflict: null, CancellationToken.None);

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
                installer.InstallAsync(release, progress: null, onConflict: null, CancellationToken.None));

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
            await installer.InstallAsync(release, progress, onConflict: null, CancellationToken.None);

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
                installer.InstallAsync(release, progress: null, onConflict: null, CancellationToken.None));
        }

        [Fact]
        public async Task InstallAsync_when_lock_held_leaves_existing_staging_intact()
        {
            // Regression: the old order called ResetStaging *before* acquiring
            // the lock, so a second installer would wipe a first installer's
            // in-flight staging directory on its way to losing the lock race.
            using var work = new TempDir();
            var installDir = Path.Combine(work.Path, "install");
            Directory.CreateDirectory(installDir);

            // Pre-populate a staging dir as if another installer were mid-download.
            var stagingDir = Path.Combine(installDir, ".update", "v1.0.0");
            Directory.CreateDirectory(stagingDir);
            var sentinel = Path.Combine(stagingDir, "in-flight-asset.zip");
            File.WriteAllBytes(sentinel, new byte[] { 1, 2, 3 });

            // Hold the lock externally to simulate that other installer.
            var lockPath = Path.Combine(installDir, ".update.lock");
            using var foreignLock = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);

            var release = BuildReleaseWithSingleAssetZip("v1.0.0", "myapp-v1.0.0-linux-x64.zip", ("a.txt", "x"));
            var source = new FakeUpdateSource
            {
                AssetBytes = _ => CreateZipBytes(("myapp-v1.0.0-linux-x64", new[] { ("a.txt", "x") })),
            };

            var installer = NewInstaller(installDir, source, "linux-x64");
            await Assert.ThrowsAsync<UpdateException>(() =>
                installer.InstallAsync(release, progress: null, onConflict: null, CancellationToken.None));

            Assert.True(File.Exists(sentinel),
                "Lock acquisition must precede any staging mutation.");
        }

        [Fact]
        public async Task InstallAsync_when_resolver_returns_malicious_name_throws()
        {
            using var work = new TempDir();
            var installDir = Path.Combine(work.Path, "install");
            Directory.CreateDirectory(installDir);

            var malicious = new ReleaseAsset(
                Name: "../escape.zip",
                DownloadUrl: new Uri("https://example.com/x"),
                SizeBytes: 100,
                ContentType: null,
                Metadata: new Dictionary<string, string>());
            var release = new RemoteRelease(
                Tag: "v1.0.0",
                Channel: null,
                ReleaseNotesUrl: null,
                Assets: new[] { malicious },
                PublishedAt: DateTimeOffset.UtcNow);

            // Use a resolver that always returns the malicious asset, bypassing
            // the default resolver's name-shape filter so we can prove the
            // installer's own ValidateAssetName runs.
            var installer = new UpdateInstaller(
                new SelfUpdaterOptions { AppName = "myapp", UseDefaultSha256Verifier = false },
                new FakeUpdateSource(),
                new AlwaysReturnAssetResolver(malicious),
                Array.Empty<IPackageVerifier>(),
                ridResolver: () => "linux-x64",
                installDirResolver: () => installDir);

            var ex = await Assert.ThrowsAsync<UpdateException>(() =>
                installer.InstallAsync(release, progress: null, onConflict: null, CancellationToken.None));

            Assert.Contains("Refusing to install asset", ex.Message, StringComparison.Ordinal);
        }

        private sealed class AlwaysReturnAssetResolver : IAssetResolver
        {
            private readonly ReleaseAsset _asset;
            public AlwaysReturnAssetResolver(ReleaseAsset asset) => _asset = asset;
            public ReleaseAsset? Resolve(RemoteRelease release, string runtimeIdentifier) => _asset;
        }

        [Theory]
        [InlineData("../etc/passwd")]
        [InlineData("..\\Windows\\System32\\cmd.exe")]
        [InlineData("..")]
        [InlineData(".")]
        [InlineData("foo/bar.zip")]
        [InlineData("foo\\bar.zip")]
        [InlineData("")]
        [InlineData("   ")]
        public void ValidateAssetName_rejects_dangerous_names(string name)
        {
            Assert.Throws<UpdateException>(() => UpdateInstaller.ValidateAssetName(name));
        }

        [Theory]
        [InlineData("/etc/passwd")]
        [InlineData("\\\\server\\share\\file.zip")]
        public void ValidateAssetName_rejects_rooted_paths(string name)
        {
            // Branched out from the above because IsPathRooted is platform-dependent
            // for Windows-style paths — both cases must throw on every platform.
            Assert.Throws<UpdateException>(() => UpdateInstaller.ValidateAssetName(name));
        }

        [Theory]
        [InlineData("myapp-v1.0.0-linux-x64.zip")]
        [InlineData("myapp-osx-arm64.tar.gz")]
        [InlineData("file.with.many.dots.zip")]
        [InlineData("a")]
        public void ValidateAssetName_accepts_valid_filenames(string name)
        {
            UpdateInstaller.ValidateAssetName(name);   // does not throw
        }

        [Fact]
        public void RestoreFromOld_moves_named_entries_back_into_install()
        {
            using var work = new TempDir();
            var installDir = work.Combine("install");
            var oldDir = work.Combine("old");
            Directory.CreateDirectory(installDir);
            Directory.CreateDirectory(oldDir);

            File.WriteAllText(Path.Combine(oldDir, "a.txt"), "alpha");
            Directory.CreateDirectory(Path.Combine(oldDir, "subdir"));
            File.WriteAllText(Path.Combine(oldDir, "subdir", "nested.txt"), "nested");

            UpdateInstaller.RestoreFromOld(oldDir, installDir, TwoEntryNames);

            Assert.Equal("alpha", File.ReadAllText(Path.Combine(installDir, "a.txt")));
            Assert.True(Directory.Exists(Path.Combine(installDir, "subdir")));
            Assert.Equal("nested", File.ReadAllText(Path.Combine(installDir, "subdir", "nested.txt")));
            Assert.False(File.Exists(Path.Combine(oldDir, "a.txt")));
        }

        [Fact]
        public void RestoreFromOld_overwrites_existing_destination_entries()
        {
            using var work = new TempDir();
            var installDir = work.Combine("install");
            var oldDir = work.Combine("old");
            Directory.CreateDirectory(installDir);
            Directory.CreateDirectory(oldDir);

            File.WriteAllText(Path.Combine(installDir, "a.txt"), "garbage from a partial copy");
            File.WriteAllText(Path.Combine(oldDir, "a.txt"), "original");

            UpdateInstaller.RestoreFromOld(oldDir, installDir, OneEntryName);

            Assert.Equal("original", File.ReadAllText(Path.Combine(installDir, "a.txt")));
        }

        [Fact]
        public void Swap_when_phase2_copy_fails_restores_install_from_old()
        {
            // Force a deterministic Phase 2 failure by including a file
            // named ".old" in the source. install/.old was just created
            // as a directory by Swap's prelude, so the File.Copy of
            // source/.old → install/.old fails. The rollback path should
            // then remove anything Phase 2 placed and move the original
            // contents back from .old/.
            using var work = new TempDir();
            var installDir = work.Combine("install");
            Directory.CreateDirectory(installDir);
            File.WriteAllText(Path.Combine(installDir, "current.txt"), "original");
            Directory.CreateDirectory(Path.Combine(installDir, "vendor"));
            File.WriteAllText(Path.Combine(installDir, "vendor", "lib.txt"), "lib v1");

            var sourceDir = work.Combine("src");
            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(Path.Combine(sourceDir, "newfile.txt"), "new content");
            File.WriteAllText(Path.Combine(sourceDir, ".old"), "should clash with the .old/ dir");

            var oldDir = Path.Combine(installDir, ".old");

            Assert.ThrowsAny<Exception>(() => UpdateInstaller.Swap(sourceDir, installDir, oldDir));

            // Original install state restored.
            Assert.True(File.Exists(Path.Combine(installDir, "current.txt")));
            Assert.Equal("original", File.ReadAllText(Path.Combine(installDir, "current.txt")));
            Assert.True(Directory.Exists(Path.Combine(installDir, "vendor")));
            Assert.Equal("lib v1", File.ReadAllText(Path.Combine(installDir, "vendor", "lib.txt")));

            // Anything Phase 2 managed to place before the failure is gone.
            Assert.False(File.Exists(Path.Combine(installDir, "newfile.txt")));
        }

        [Theory]
        [InlineData("appsettings.Development.json", "appsettings.Development.json", true)]
        [InlineData("appsettings.*.json", "appsettings.Development.json", true)]
        [InlineData("appsettings.*.json", "appsettings.json", false)]
        [InlineData("data/**", "data", true)]
        [InlineData("data/**", "other", false)]
        [InlineData("data/seed.json", "data", true)]   // top-level rule: pattern's first segment matches dir name
        [InlineData("*.db", "myapp.db", true)]
        [InlineData("*.db", "config.json", false)]
        [InlineData("", "anything", false)]            // empty pattern is ignored
        public void IsPreserved_matches_top_level_entry_against_pattern(string pattern, string name, bool expected)
        {
            var patterns = new[] { pattern };
            Assert.Equal(expected, UpdateInstaller.IsPreserved(name, patterns));
        }

        [Fact]
        public void IsPreserved_with_empty_list_returns_false()
        {
            Assert.False(UpdateInstaller.IsPreserved("anything.txt", Array.Empty<string>()));
        }

        [Fact]
        public async Task SwapAsync_preserved_entry_is_not_moved_to_old()
        {
            using var work = new TempDir();
            var installDir = work.Combine("install");
            var sourceDir = work.Combine("src");
            var oldDir = Path.Combine(installDir, ".old");
            Directory.CreateDirectory(installDir);
            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(Path.Combine(installDir, "appsettings.Development.json"), "user-config");
            File.WriteAllText(Path.Combine(installDir, "binary.exe"), "old-binary");
            File.WriteAllText(Path.Combine(sourceDir, "binary.exe"), "new-binary");

            await UpdateInstaller.SwapAsync(
                sourceDir, installDir, oldDir,
                preservePaths: PreserveAppsettingsDevelopment,
                onConflict: null,
                CancellationToken.None);

            // Preserved file untouched.
            Assert.Equal("user-config", File.ReadAllText(Path.Combine(installDir, "appsettings.Development.json")));
            Assert.False(File.Exists(Path.Combine(oldDir, "appsettings.Development.json")));
            // Non-preserved file replaced.
            Assert.Equal("new-binary", File.ReadAllText(Path.Combine(installDir, "binary.exe")));
            Assert.Equal("old-binary", File.ReadAllText(Path.Combine(oldDir, "binary.exe")));
        }

        [Fact]
        public async Task SwapAsync_when_release_conflicts_default_keeps_existing()
        {
            using var work = new TempDir();
            var installDir = work.Combine("install");
            var sourceDir = work.Combine("src");
            var oldDir = Path.Combine(installDir, ".old");
            Directory.CreateDirectory(installDir);
            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(Path.Combine(installDir, "appsettings.json"), "user-edited");
            File.WriteAllText(Path.Combine(sourceDir, "appsettings.json"), "release-default");

            await UpdateInstaller.SwapAsync(
                sourceDir, installDir, oldDir,
                preservePaths: PreserveAppsettingsJson,
                onConflict: null,                       // null → keep existing
                CancellationToken.None);

            Assert.Equal("user-edited", File.ReadAllText(Path.Combine(installDir, "appsettings.json")));
            Assert.False(File.Exists(Path.Combine(oldDir, "appsettings.json")));
        }

        [Fact]
        public async Task SwapAsync_when_resolver_returns_use_new_replaces_existing()
        {
            using var work = new TempDir();
            var installDir = work.Combine("install");
            var sourceDir = work.Combine("src");
            var oldDir = Path.Combine(installDir, ".old");
            Directory.CreateDirectory(installDir);
            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(Path.Combine(installDir, "appsettings.json"), "user-edited");
            File.WriteAllText(Path.Combine(sourceDir, "appsettings.json"), "release-default");

            UpdateConflict? sawConflict = null;
            Func<UpdateConflict, CancellationToken, Task<UpdateConflictResolution>> resolver =
                (c, _) => { sawConflict = c; return Task.FromResult(UpdateConflictResolution.UseNew); };

            await UpdateInstaller.SwapAsync(
                sourceDir, installDir, oldDir,
                preservePaths: PreserveAppsettingsJson,
                onConflict: resolver,
                CancellationToken.None);

            Assert.NotNull(sawConflict);
            Assert.Equal("appsettings.json", sawConflict!.RelativePath);
            Assert.Equal("release-default", File.ReadAllText(Path.Combine(installDir, "appsettings.json")));
            // Previous user copy moved to .old/ (so the next-startup cleanup sweep removes it).
            Assert.Equal("user-edited", File.ReadAllText(Path.Combine(oldDir, "appsettings.json")));
        }

        [Fact]
        public async Task SwapAsync_preserved_glob_does_not_block_unrelated_release_files()
        {
            using var work = new TempDir();
            var installDir = work.Combine("install");
            var sourceDir = work.Combine("src");
            var oldDir = Path.Combine(installDir, ".old");
            Directory.CreateDirectory(installDir);
            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(Path.Combine(installDir, "myapp.db"), "user-db");
            File.WriteAllText(Path.Combine(installDir, "binary.exe"), "old-binary");
            File.WriteAllText(Path.Combine(sourceDir, "binary.exe"), "new-binary");
            // Note: source does NOT ship myapp.db.

            await UpdateInstaller.SwapAsync(
                sourceDir, installDir, oldDir,
                preservePaths: PreserveDb,
                onConflict: null,
                CancellationToken.None);

            Assert.Equal("user-db", File.ReadAllText(Path.Combine(installDir, "myapp.db")));
            Assert.Equal("new-binary", File.ReadAllText(Path.Combine(installDir, "binary.exe")));
        }

        [Fact]
        public async Task SwapAsync_preserved_path_introduced_by_new_release_when_user_has_none_just_copies()
        {
            using var work = new TempDir();
            var installDir = work.Combine("install");
            var sourceDir = work.Combine("src");
            var oldDir = Path.Combine(installDir, ".old");
            Directory.CreateDirectory(installDir);
            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(Path.Combine(sourceDir, "appsettings.Production.json"), "release-default");

            // The path is preservable but the user doesn't have a copy yet —
            // installer should just place the new file with no resolver call.
            var resolverCalled = false;
            Func<UpdateConflict, CancellationToken, Task<UpdateConflictResolution>> resolver =
                (_, _) => { resolverCalled = true; return Task.FromResult(UpdateConflictResolution.KeepExisting); };

            await UpdateInstaller.SwapAsync(
                sourceDir, installDir, oldDir,
                preservePaths: PreserveAppsettingsGlob,
                onConflict: resolver,
                CancellationToken.None);

            Assert.False(resolverCalled);
            Assert.Equal("release-default", File.ReadAllText(Path.Combine(installDir, "appsettings.Production.json")));
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
