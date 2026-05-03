using System.Linq;

namespace NextIteration.SpectreConsole.SelfUpdate.Pipeline
{
    /// <summary>
    /// Default <see cref="IUpdateInstaller"/>. Stages the download under
    /// <c>&lt;install&gt;/.update/&lt;tag&gt;/</c>, runs every registered
    /// <see cref="IPackageVerifier"/>, extracts the archive, and atomically
    /// moves the previous install into <c>.old/</c> while copying the new
    /// files in. <see cref="CleanupOldInstall"/> removes <c>.old/</c> on
    /// the next process startup — the running new binary is sufficient
    /// proof the swap succeeded.
    /// </summary>
    internal sealed class UpdateInstaller : IUpdateInstaller
    {
        private const string StagingDirName = ".update";
        private const string OldDirName = ".old";
        private const string LockFileName = ".update.lock";

        private readonly SelfUpdaterOptions _options;
        private readonly IUpdateSource _source;
        private readonly IAssetResolver _resolver;
        private readonly IEnumerable<IPackageVerifier> _verifiers;
        private readonly Func<string> _ridResolver;
        private readonly Func<string> _installDirResolver;

        public UpdateInstaller(
            SelfUpdaterOptions options,
            IUpdateSource source,
            IAssetResolver resolver,
            IEnumerable<IPackageVerifier> verifiers)
            : this(options, source, resolver, verifiers, RuntimeIdentifier.Detect, DefaultInstallDirectory)
        {
        }

        internal UpdateInstaller(
            SelfUpdaterOptions options,
            IUpdateSource source,
            IAssetResolver resolver,
            IEnumerable<IPackageVerifier> verifiers,
            Func<string> ridResolver,
            Func<string> installDirResolver)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(resolver);
            ArgumentNullException.ThrowIfNull(verifiers);
            ArgumentNullException.ThrowIfNull(ridResolver);
            ArgumentNullException.ThrowIfNull(installDirResolver);

            _options = options;
            _source = source;
            _resolver = resolver;
            _verifiers = verifiers;
            _ridResolver = ridResolver;
            _installDirResolver = installDirResolver;
        }

        public string InstallDirectory => _installDirResolver();

        public async Task InstallAsync(
            RemoteRelease release,
            IProgress<UpdateProgressEvent>? progress = null,
            Func<UpdateConflict, CancellationToken, Task<UpdateConflictResolution>>? onConflict = null,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(release);

            var rid = _ridResolver();
            var asset = _resolver.Resolve(release, rid)
                ?? throw new UpdateException(BuildNoMatchMessage(release, rid));

            // Validate up front — reject path traversal / rooted / multi-segment
            // names from the source so nothing it publishes can write outside
            // the staging directory.
            ValidateAssetName(asset.Name);

            var installDir = InstallDirectory;
            var stagingDir = Path.Combine(installDir, StagingDirName, SanitizeTag(release.Tag));
            var oldDir = Path.Combine(installDir, OldDirName);
            var lockFile = Path.Combine(installDir, LockFileName);

            // Acquire the lock BEFORE touching the staging tree. Otherwise a
            // second installer can wipe a first installer's in-flight staging
            // directory on its way to losing the lock race, corrupting the
            // first install.
            Directory.CreateDirectory(installDir);
            using var lockStream = InstallLock.Acquire(lockFile, installDir);

            ResetStaging(stagingDir);

            try
            {
                var archivePath = Path.Combine(stagingDir, asset.Name);

                using (var dlCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    dlCts.CancelAfter(_options.DownloadTimeout);
                    progress?.Report(new UpdateProgressEvent(UpdateStage.Downloading, 0));
                    await DownloadAssetAsync(asset, archivePath, progress, dlCts.Token).ConfigureAwait(false);
                    progress?.Report(new UpdateProgressEvent(UpdateStage.Downloading, 1));
                }

                progress?.Report(new UpdateProgressEvent(UpdateStage.Verifying, 0));
                foreach (var verifier in _verifiers)
                {
                    await verifier.VerifyAsync(archivePath, release, asset, ct).ConfigureAwait(false);
                }
                progress?.Report(new UpdateProgressEvent(UpdateStage.Verifying, 1));

                progress?.Report(new UpdateProgressEvent(UpdateStage.Extracting, 0));
                var extractedDir = Path.Combine(stagingDir, "extracted");
                await ArchiveExtractor.ExtractAsync(archivePath, extractedDir, ct).ConfigureAwait(false);
                var sourceDir = ResolveSourceDirectory(extractedDir);
                progress?.Report(new UpdateProgressEvent(UpdateStage.Extracting, 1));

                progress?.Report(new UpdateProgressEvent(UpdateStage.Swapping, 0));
                await SwapAsync(sourceDir, installDir, oldDir, _options.PreservePaths, onConflict, ct).ConfigureAwait(false);
                progress?.Report(new UpdateProgressEvent(UpdateStage.Swapping, 1));
            }
            finally
            {
                // Drop the whole .update/ tree, not just the leaf <tag>/, so
                // we don't leave an empty parent behind on the install dir.
                TryDeleteDirectory(Path.Combine(installDir, StagingDirName));
            }
        }

        public void CleanupOldInstall()
        {
            try
            {
                var oldDir = Path.Combine(InstallDirectory, OldDirName);
                if (Directory.Exists(oldDir))
                {
                    Directory.Delete(oldDir, recursive: true);
                }
            }
            catch
            {
                // Non-fatal — will retry on next startup.
            }
        }

        // ---------- Pipeline helpers (testable via InternalsVisibleTo) ----------

        internal async Task DownloadAssetAsync(ReleaseAsset asset, string destinationPath, IProgress<UpdateProgressEvent>? progress, CancellationToken ct)
        {
            await using var fileStream = File.Create(destinationPath);
            var downloadProgress = new Progress<DownloadProgress>(dp =>
            {
                if (progress is null) return;
                if (dp.TotalBytes is { } total && total > 0)
                {
                    progress.Report(new UpdateProgressEvent(
                        UpdateStage.Downloading,
                        Math.Min(1.0, (double)dp.BytesDownloaded / total)));
                }
            });
            await _source.DownloadAssetAsync(asset, fileStream, downloadProgress, ct).ConfigureAwait(false);
        }

        internal static string ResolveSourceDirectory(string extractedDirectory)
        {
            // Most release archives wrap a single top-level folder; if so,
            // descend into it. Otherwise treat the extracted root as the
            // source directly.
            var entries = Directory.EnumerateFileSystemEntries(extractedDirectory).ToArray();
            if (entries.Length == 1 && Directory.Exists(entries[0]))
            {
                return entries[0];
            }
            return extractedDirectory;
        }

        // Backwards-compatible synchronous Swap, retained for callers that
        // don't need preserve-path or conflict-resolution semantics.
        // Equivalent to SwapAsync(..., preservePaths: empty, onConflict: null).
        internal static void Swap(string sourceDirectory, string installDirectory, string oldDirectory) =>
            SwapAsync(sourceDirectory, installDirectory, oldDirectory, Array.Empty<string>(), onConflict: null, CancellationToken.None)
                .GetAwaiter().GetResult();

        internal static async Task SwapAsync(
            string sourceDirectory,
            string installDirectory,
            string oldDirectory,
            IReadOnlyList<string> preservePaths,
            Func<UpdateConflict, CancellationToken, Task<UpdateConflictResolution>>? onConflict,
            CancellationToken ct)
        {
            // Reset .old/ — it should be empty going in.
            if (Directory.Exists(oldDirectory))
            {
                Directory.Delete(oldDirectory, recursive: true);
            }
            Directory.CreateDirectory(oldDirectory);

            // Phase 1: move install/ contents (except maintenance and
            // preserved entries) → .old/. Track what we moved so a failure
            // mid-loop can restore the partial move and leave the install
            // dir as we found it.
            var movedNames = new List<string>();
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(installDirectory))
                {
                    var name = Path.GetFileName(entry);
                    if (IsMaintenanceEntry(name)) continue;
                    if (IsPreserved(name, preservePaths)) continue;

                    var dest = Path.Combine(oldDirectory, name);
                    if (File.Exists(entry))
                    {
                        File.Move(entry, dest);
                    }
                    else
                    {
                        Directory.Move(entry, dest);
                    }
                    movedNames.Add(name);
                }
            }
            catch
            {
                RestoreFromOld(oldDirectory, installDirectory, movedNames);
                throw;
            }

            // Phase 2: copy new entries in. When a new entry lands on a
            // preserved path, ask the resolver (or default to KeepExisting).
            // Track each placed entry so a failure mid-copy can rip out
            // the partial replacement and restore the previous install
            // from .old/.
            var copiedNames = new List<string>();
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(sourceDirectory))
                {
                    ct.ThrowIfCancellationRequested();
                    var name = Path.GetFileName(entry);
                    var dest = Path.Combine(installDirectory, name);

                    // Conflict path: source ships an entry whose top-level
                    // name matches a preserved pattern.
                    if (IsPreserved(name, preservePaths))
                    {
                        var existingExists = File.Exists(dest) || Directory.Exists(dest);
                        if (existingExists)
                        {
                            var resolution = await ResolveConflictAsync(name, dest, entry, onConflict, ct).ConfigureAwait(false);
                            if (resolution == UpdateConflictResolution.KeepExisting)
                            {
                                // User's file wins — drop the new entry on the floor.
                                continue;
                            }
                            // UseNew: the existing preserved entry wasn't
                            // moved in Phase 1; do it inline now so the
                            // copy below has somewhere clean to land and
                            // rollback restores the right thing on failure.
                            var oldDest = Path.Combine(oldDirectory, name);
                            TryDeleteEntry(oldDest);
                            if (File.Exists(dest)) File.Move(dest, oldDest);
                            else if (Directory.Exists(dest)) Directory.Move(dest, oldDest);
                            movedNames.Add(name);
                        }
                        // else: a new release introduces a path that the
                        // user marked as preservable but doesn't exist
                        // locally. Treat as a normal copy.
                    }

                    if (File.Exists(entry))
                    {
                        File.Copy(entry, dest, overwrite: true);
                    }
                    else
                    {
                        CopyDirectory(entry, dest);
                    }
                    copiedNames.Add(name);
                }
            }
            catch
            {
                foreach (var name in copiedNames)
                {
                    TryDeleteEntry(Path.Combine(installDirectory, name));
                }
                RestoreFromOld(oldDirectory, installDirectory, movedNames);
                throw;
            }
        }

        private static async Task<UpdateConflictResolution> ResolveConflictAsync(
            string name,
            string existingPath,
            string newPath,
            Func<UpdateConflict, CancellationToken, Task<UpdateConflictResolution>>? onConflict,
            CancellationToken ct)
        {
            if (onConflict is null)
            {
                return UpdateConflictResolution.KeepExisting;
            }

            long? existingSize = File.Exists(existingPath) ? new FileInfo(existingPath).Length : null;
            long? newSize = File.Exists(newPath) ? new FileInfo(newPath).Length : null;
            var conflict = new UpdateConflict(name.Replace(Path.DirectorySeparatorChar, '/'), existingSize, newSize);
            return await onConflict(conflict, ct).ConfigureAwait(false);
        }

        internal static bool IsPreserved(string name, IReadOnlyList<string> preservePaths)
        {
            if (preservePaths.Count == 0) return false;
            foreach (var pattern in preservePaths)
            {
                if (string.IsNullOrWhiteSpace(pattern)) continue;
                // Take the part of the pattern before the first slash —
                // this lets `data/**`, `data/seed.json`, and bare `data`
                // all match the top-level entry `data`. Nested-only
                // preservation (e.g. preserve only `data/seed.json` but
                // not the rest of `data/`) is out of scope for v0.1.x.
                var head = TopLevelSegment(pattern);
                if (head.Length == 0) continue;
                if (System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(head, name, ignoreCase: true))
                {
                    return true;
                }
            }
            return false;
        }

        private static ReadOnlySpan<char> TopLevelSegment(string pattern)
        {
            ReadOnlySpan<char> span = pattern;
            for (var i = 0; i < span.Length; i++)
            {
                if (span[i] == '/' || span[i] == '\\') return span[..i];
            }
            return span;
        }

        // Move every named entry from .old/ back into install/, replacing
        // anything currently at the destination. Best-effort — any single
        // entry that can't be restored is logged silently; the original
        // pipeline exception still surfaces to the caller, which is the
        // useful signal.
        internal static void RestoreFromOld(string oldDirectory, string installDirectory, IReadOnlyList<string> names)
        {
            foreach (var name in names)
            {
                var src = Path.Combine(oldDirectory, name);
                var dest = Path.Combine(installDirectory, name);
                try
                {
                    TryDeleteEntry(dest);
                    if (File.Exists(src))
                    {
                        File.Move(src, dest);
                    }
                    else if (Directory.Exists(src))
                    {
                        Directory.Move(src, dest);
                    }
                }
                catch
                {
                    // Best effort — partial-restore failures are surfaced
                    // implicitly through the pipeline exception.
                }
            }
        }

        private static void TryDeleteEntry(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            }
            catch
            {
                // Best effort.
            }
        }

        internal static bool IsMaintenanceEntry(string name) =>
            string.Equals(name, StagingDirName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, OldDirName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, LockFileName, StringComparison.OrdinalIgnoreCase);

        internal static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, file);
                var target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }
        }

        internal static void ValidateAssetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new UpdateException("Refusing to install: release asset has no name.");
            }
            if (name.Contains('\0', StringComparison.Ordinal))
            {
                throw new UpdateException($"Refusing to install asset '{name}': contains a null character.");
            }
            if (name.Contains('/', StringComparison.Ordinal)
                || name.Contains('\\', StringComparison.Ordinal)
                || name.Equals("..", StringComparison.Ordinal)
                || name.Equals(".", StringComparison.Ordinal))
            {
                throw new UpdateException(
                    $"Refusing to install asset '{name}': name must be a single file segment with no path separators or parent references.");
            }
            if (Path.IsPathRooted(name))
            {
                throw new UpdateException(
                    $"Refusing to install asset '{name}': name must not be a rooted path.");
            }
            if (!string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal))
            {
                throw new UpdateException(
                    $"Refusing to install asset '{name}': resolves to a different file segment.");
            }
        }

        internal static string SanitizeTag(string tag)
        {
            // Stage directory names need to be filesystem-safe across all OSs.
            // Stripping path separators and a few weird characters is enough —
            // tags are typically already safe.
            var sb = new System.Text.StringBuilder(tag.Length);
            foreach (var c in tag)
            {
                sb.Append(IsSafe(c) ? c : '_');
            }
            return sb.Length == 0 ? "tag" : sb.ToString();
        }

        private static bool IsSafe(char c) =>
            char.IsLetterOrDigit(c) || c is '.' or '-' or '_' or '+';

        private static void ResetStaging(string stagingDir)
        {
            try
            {
                if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, recursive: true);
            }
            catch (Exception ex)
            {
                throw new UpdateException(
                    $"Unable to reset staging directory '{stagingDir}': {ex.Message}", ex);
            }
            Directory.CreateDirectory(stagingDir);
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            }
            catch
            {
                // Best effort — staging is under .update/ which the next install or
                // a subsequent CleanupOldInstall pass will eventually overwrite.
            }
        }

        private static string DefaultInstallDirectory() =>
            AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        private static string BuildNoMatchMessage(RemoteRelease release, string rid)
        {
            var available = release.Assets.Count == 0
                ? "(release has no assets)"
                : string.Join(", ", release.Assets.Select(a => a.Name));
            return $"No release asset matches RID '{rid}' on tag '{release.Tag}'. Available assets: {available}.";
        }
    }
}
