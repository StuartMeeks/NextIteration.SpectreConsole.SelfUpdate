namespace NextIteration.SpectreConsole.SelfUpdate
{
    /// <summary>
    /// The pipeline stages an <see cref="IUpdateInstaller"/> moves through
    /// while applying an update. Each value is reported once when the stage
    /// starts and may be reported again with progressively higher
    /// <see cref="UpdateProgressEvent.PercentComplete"/> values during the
    /// stage's work.
    /// </summary>
    public enum UpdateStage
    {
        /// <summary>Streaming the asset from the update source to local staging.</summary>
        Downloading,

        /// <summary>Running every registered <see cref="IPackageVerifier"/> against the staged file.</summary>
        Verifying,

        /// <summary>Expanding the staged archive into a temporary directory.</summary>
        Extracting,

        /// <summary>Atomically moving the new files into the install directory.</summary>
        Swapping,

        /// <summary>Deleting the previous install (the <c>.old/</c> directory) on next startup.</summary>
        CleaningUp,
    }
}
