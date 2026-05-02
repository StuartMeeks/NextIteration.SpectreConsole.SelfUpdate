using NextIteration.SpectreConsole.SelfUpdate.Pipeline;
using NextIteration.SpectreConsole.SelfUpdate.Tests.Infrastructure;

using Xunit;

namespace NextIteration.SpectreConsole.SelfUpdate.Tests.Pipeline
{
    public sealed class InstallLockTests
    {
        [Fact]
        public void Acquire_on_fresh_path_returns_open_stream()
        {
            using var dir = new TempDir();
            var lockPath = Path.Combine(dir.Path, ".update.lock");

            using var stream = InstallLock.Acquire(lockPath, dir.Path);

            Assert.NotNull(stream);
            Assert.True(File.Exists(lockPath));
        }

        [Fact]
        public void Acquire_when_lock_already_held_throws_in_progress()
        {
            using var dir = new TempDir();
            var lockPath = Path.Combine(dir.Path, ".update.lock");

            using var first = InstallLock.Acquire(lockPath, dir.Path);

            var ex = Assert.Throws<UpdateException>(() => InstallLock.Acquire(lockPath, dir.Path));
            Assert.Contains("another update is already in progress", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Disposing_first_lock_removes_file_and_allows_re_acquisition()
        {
            using var dir = new TempDir();
            var lockPath = Path.Combine(dir.Path, ".update.lock");

            using (var first = InstallLock.Acquire(lockPath, dir.Path))
            {
                Assert.True(File.Exists(lockPath));
            }

            // DeleteOnClose drops the file when the stream is disposed.
            Assert.False(File.Exists(lockPath));

            // A subsequent Acquire on the same path should now succeed.
            using var second = InstallLock.Acquire(lockPath, dir.Path);
            Assert.NotNull(second);
        }

        [Fact]
        public void Acquire_rejects_blank_paths()
        {
            Assert.Throws<ArgumentException>(() => InstallLock.Acquire("", "/tmp"));
            Assert.Throws<ArgumentException>(() => InstallLock.Acquire("   ", "/tmp"));
            Assert.Throws<ArgumentException>(() => InstallLock.Acquire("/tmp/x.lock", ""));
        }

        [Fact]
        public void Acquire_when_directory_not_writable_throws_not_writable()
        {
            // Read-only-directory enforcement on Windows is awkward (chmod has no
            // direct equivalent and Acquire creates the file with FileMode.CreateNew,
            // which Windows still permits inside a read-only folder for the owning
            // user). Skip this test on Windows; Linux/macOS coverage is enough.
            if (OperatingSystem.IsWindows()) return;

            using var dir = new TempDir();
            var sub = Path.Combine(dir.Path, "ro");
            Directory.CreateDirectory(sub);
            try
            {
                File.SetUnixFileMode(sub, UnixFileMode.UserRead | UnixFileMode.UserExecute);

                var ex = Assert.Throws<UpdateException>(() =>
                    InstallLock.Acquire(Path.Combine(sub, ".update.lock"), sub));
                Assert.Contains("not writable", ex.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                // Restore writability so TempDir.Dispose() can clean up.
                try { File.SetUnixFileMode(sub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
                catch { /* best effort */ }
            }
        }
    }
}
