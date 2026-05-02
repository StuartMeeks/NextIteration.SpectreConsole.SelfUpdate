namespace NextIteration.SpectreConsole.SelfUpdate.Pipeline
{
    /// <summary>
    /// Mutex guarding a single install directory: any concurrent
    /// <see cref="UpdateInstaller"/> across processes loses the race and
    /// surfaces a clear "another update is already in progress" message.
    /// The underlying file is created with <c>FileShare.None</c> +
    /// <c>FileOptions.DeleteOnClose</c>, so an aborted process drops the
    /// lock automatically when its handle is released.
    /// </summary>
    internal static class InstallLock
    {
        public static FileStream Acquire(string lockFilePath, string installDirectory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(lockFilePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);

            try
            {
                return new FileStream(
                    lockFilePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 1,
                    options: FileOptions.DeleteOnClose);
            }
            catch (IOException ex)
            {
                throw new UpdateException(
                    "Another update is already in progress (lock file exists). Wait for the other update to finish, or delete '"
                    + lockFilePath + "' if no install is actually running.",
                    ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new UpdateException(
                    $"Install directory is not writable: {installDirectory}",
                    ex);
            }
        }
    }
}
