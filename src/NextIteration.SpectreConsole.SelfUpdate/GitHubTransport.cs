namespace NextIteration.SpectreConsole.SelfUpdate
{
    /// <summary>
    /// Selects between the two built-in transports for the GitHub Releases
    /// update source.
    /// </summary>
    public enum GitHubTransport
    {
        /// <summary>
        /// Default. Uses <see cref="System.Net.Http.HttpClient"/> against the
        /// public GitHub REST API. No external dependencies; works for public
        /// repos out of the box. Optionally honours a personal access token
        /// via <see cref="SelfUpdaterOptions.GitHubToken"/> or the
        /// <c>GITHUB_TOKEN</c> environment variable for higher rate limits or
        /// fine-grained access.
        /// </summary>
        HttpClient,

        /// <summary>
        /// Shells out to the GitHub <c>gh</c> CLI. Useful for private repos
        /// where the developer's existing <c>gh auth</c> session is the
        /// simplest path to authenticated API + asset access. Requires
        /// <c>gh</c> on <c>PATH</c>.
        /// </summary>
        GhCli,
    }
}
