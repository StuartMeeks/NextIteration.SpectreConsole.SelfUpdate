namespace NextIteration.SpectreConsole.SelfUpdate.Internal
{
    /// <summary>
    /// Process-wide record of what the update UX has already done in this
    /// run, read by <see cref="UpdateBanner.RenderIfAvailable"/> so the
    /// post-run banner stays quiet after an update command has shown (or
    /// installed) the latest release.
    /// </summary>
    /// <remarks>
    /// Static rather than DI-scoped: the banner helper takes no service
    /// provider, and the commands' public constructors cannot take an
    /// internal state type. A plain <see cref="AsyncLocal{T}"/> would not
    /// work either — a value set inside <c>app.Run</c>'s async flow does not
    /// flow back out to <c>Main</c>.
    /// </remarks>
    internal static class UpdateSession
    {
        private sealed class State
        {
            public bool Handled;
            public string? InstalledTag;
        }

        private static readonly State ProcessState = new();

        // Test-only override. Tests run in parallel and share the process, so
        // each test that reads or asserts this state opens its own scope; the
        // scope's State object is inherited by every async flow the test
        // starts, so commands and the banner inside the test see it.
        private static readonly AsyncLocal<State?> TestScope = new();

        private static State Current => TestScope.Value ?? ProcessState;

        /// <summary>The update UX has shown the latest release in this process.</summary>
        public static bool IsHandled => Volatile.Read(ref Current.Handled);

        /// <summary>Tag of the release installed by this process, if any.</summary>
        public static string? InstalledTag => Volatile.Read(ref Current.InstalledTag);

        public static void MarkHandled() => Volatile.Write(ref Current.Handled, true);

        public static void RecordInstalled(string tag)
        {
            ArgumentNullException.ThrowIfNull(tag);
            Volatile.Write(ref Current.InstalledTag, tag);
        }

        /// <summary>
        /// Isolate this state for the current async flow until the returned
        /// scope is disposed. Tests only.
        /// </summary>
        internal static IDisposable BeginTestScope()
        {
            var previous = TestScope.Value;
            TestScope.Value = new State();
            return new Scope(previous);
        }

        private sealed class Scope(State? previous) : IDisposable
        {
            public void Dispose() => TestScope.Value = previous;
        }
    }
}
