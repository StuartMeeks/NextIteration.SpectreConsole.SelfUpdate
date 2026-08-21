namespace NextIteration.SpectreConsole.SelfUpdate.Tests.Infrastructure
{
    /// <summary>
    /// <see cref="IProgress{T}"/> recorder that captures reports inline.
    /// <para>
    /// <see cref="Progress{T}"/> is the wrong double for a test: it posts its
    /// callback to the captured <see cref="SynchronizationContext"/>, or to the
    /// thread pool when there is none — which is the case under xUnit v3. The
    /// installer reports every stage boundary synchronously, but the byte-level
    /// download reports come through its own internal <see cref="Progress{T}"/>
    /// and can therefore land <em>after</em> the awaited call returns, mutating
    /// the collection a test is asserting over.
    /// </para>
    /// <para>
    /// Recording inline under a lock and asserting over <see cref="Snapshot"/>
    /// removes both hazards: no unsynchronised writes, and no enumeration of a
    /// collection a late report can still append to.
    /// </para>
    /// </summary>
    internal sealed class RecordingProgress<T> : IProgress<T>
    {
        private readonly List<T> _reports = [];

        /// <summary>Everything reported so far, as a point-in-time copy.</summary>
        public IReadOnlyList<T> Snapshot
        {
            get
            {
                lock (_reports)
                {
                    return [.. _reports];
                }
            }
        }

        public void Report(T value)
        {
            lock (_reports)
            {
                _reports.Add(value);
            }
        }
    }
}
