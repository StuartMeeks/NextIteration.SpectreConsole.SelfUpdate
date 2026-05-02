namespace NextIteration.SpectreConsole.SelfUpdate.Tests.Infrastructure
{
    /// <summary>
    /// Disposable scope wrapping a fresh per-test working directory.
    /// Mirrors the helper used in the sibling Auth/Auth.Providers tests:
    /// the directory is created on construction under
    /// <see cref="Path.GetTempPath"/>, and recursively deleted on
    /// disposal — failures during cleanup are swallowed so a flaky
    /// filesystem can't fail tests.
    /// </summary>
    internal sealed class TempDir : IDisposable
    {
        public string Path { get; }

        public TempDir(string? prefix = null)
        {
            var name = (prefix ?? "selfupdate") + "-" + Guid.NewGuid().ToString("N");
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), name);
            Directory.CreateDirectory(Path);
        }

        public string Combine(params string[] parts) => System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // Best effort.
            }
        }
    }
}
