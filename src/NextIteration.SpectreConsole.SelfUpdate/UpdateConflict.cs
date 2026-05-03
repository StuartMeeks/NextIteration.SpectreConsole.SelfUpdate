namespace NextIteration.SpectreConsole.SelfUpdate
{
    /// <summary>
    /// Describes a single file conflict surfaced by the installer to a
    /// caller-supplied resolver: a new release ships an entry whose path
    /// matches one of <see cref="SelfUpdaterOptions.PreservePaths"/>, so
    /// the installer needs to know whether to keep the user's local copy
    /// or overwrite it with the new release's version.
    /// </summary>
    /// <param name="RelativePath">
    /// Path of the conflicting entry, relative to the install directory
    /// (e.g. <c>"appsettings.Development.json"</c> or <c>"data/seed.json"</c>).
    /// Forward-slash separated regardless of OS so resolvers can match
    /// against patterns portably.
    /// </param>
    /// <param name="ExistingSizeBytes">
    /// Size of the user's existing file in bytes, or <see langword="null"/>
    /// when the existing entry is a directory.
    /// </param>
    /// <param name="NewSizeBytes">
    /// Size of the new release's file in bytes, or <see langword="null"/>
    /// when the new entry is a directory.
    /// </param>
    public sealed record UpdateConflict(
        string RelativePath,
        long? ExistingSizeBytes,
        long? NewSizeBytes);
}
