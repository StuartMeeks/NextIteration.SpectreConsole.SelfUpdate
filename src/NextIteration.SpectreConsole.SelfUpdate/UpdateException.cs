namespace NextIteration.SpectreConsole.SelfUpdate
{
    /// <summary>
    /// Thrown by <see cref="IUpdateInstaller"/> when a download / verify /
    /// extract / swap step fails. Sources surface their own failures via
    /// this exception too — consumers can <c>catch (UpdateException)</c> to
    /// handle every failure mode the package itself signals, separately from
    /// transport-level <see cref="System.Net.Http.HttpRequestException"/> /
    /// <see cref="System.IO.IOException"/> exceptions surfaced from below.
    /// </summary>
    public sealed class UpdateException : Exception
    {
        /// <summary>Initializes a new instance of <see cref="UpdateException"/>.</summary>
        public UpdateException()
        {
        }

        /// <summary>Initializes a new instance of <see cref="UpdateException"/> with a message.</summary>
        public UpdateException(string message)
            : base(message)
        {
        }

        /// <summary>Initializes a new instance of <see cref="UpdateException"/> with a message and inner exception.</summary>
        public UpdateException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
