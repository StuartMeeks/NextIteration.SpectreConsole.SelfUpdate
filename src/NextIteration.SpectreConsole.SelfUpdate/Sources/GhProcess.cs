using System.Diagnostics;

namespace NextIteration.SpectreConsole.SelfUpdate.Sources
{
    /// <summary>
    /// Internal helper that orchestrates a single <c>gh</c> invocation:
    /// launches the process with no shell window, captures both streams,
    /// honours a per-call timeout, and surfaces clear "is gh installed?"
    /// errors when launch itself fails. Arguments are passed via
    /// <see cref="ProcessStartInfo.ArgumentList"/> so callers don't need to
    /// quote or escape values that came from a remote source.
    /// </summary>
    internal static class GhProcess
    {
        public static async Task<string> RunCaptureStdoutAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(arguments);
            if (arguments.Count == 0)
            {
                throw new ArgumentException("At least one gh subcommand must be supplied.", nameof(arguments));
            }

            var psi = new ProcessStartInfo("gh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in arguments)
            {
                psi.ArgumentList.Add(arg);
            }

            Process proc;
            try
            {
                var started = Process.Start(psi)
                    ?? throw new GhProcessException("Failed to launch gh.");
                proc = started;
            }
            catch (Exception ex) when (ex is not GhProcessException)
            {
                throw new GhProcessException(
                    $"Failed to launch gh: {ex.Message}. Is the GitHub CLI installed and on PATH?",
                    ex);
            }

            using (proc)
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linked.CancelAfter(timeout);

                var stdoutTask = proc.StandardOutput.ReadToEndAsync(linked.Token);
                var stderrTask = proc.StandardError.ReadToEndAsync(linked.Token);

                try
                {
                    await proc.WaitForExitAsync(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    TryKill(proc);
                    throw new GhProcessException($"gh {arguments[0]} timed out after {timeout}.");
                }

                var stdout = await stdoutTask.ConfigureAwait(false);
                var stderr = await stderrTask.ConfigureAwait(false);

                if (proc.ExitCode != 0)
                {
                    throw new GhProcessException(
                        $"gh {arguments[0]} exited with code {proc.ExitCode}: {stderr.Trim()}");
                }
                return stdout;
            }
        }

        private static void TryKill(Process proc)
        {
            try { proc.Kill(entireProcessTree: true); }
            catch
            {
                // Best effort — the process may already have exited.
            }
        }
    }

    /// <summary>Internal exception type carrying gh-specific context.</summary>
    internal sealed class GhProcessException : Exception
    {
        public GhProcessException(string message) : base(message) { }
        public GhProcessException(string message, Exception inner) : base(message, inner) { }
    }
}
