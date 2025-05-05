namespace AdfsAuth
{
    /// <summary>
    /// Interface for the ADFS authentication service.
    /// </summary>
    public interface IAdfsAuthService
    {
        /// <summary>
        /// Authenticates with ADFS using the configured options.
        /// </summary>
        /// <returns>The authentication result.</returns>
        Task<AdfsAuthResult> AuthenticateAsync();

        /// <summary>
        /// Authenticates with ADFS using the specified options.
        /// </summary>
        /// <param name="options">The authentication options to use.</param>
        /// <returns>The authentication result.</returns>
        Task<AdfsAuthResult> AuthenticateAsync(AdfsAuthOptions options);

        /// <summary>
        /// Gets the current cached tokens, or null if no tokens are cached.
        /// </summary>
        /// <returns>The cached tokens, or null if no tokens are cached.</returns>
        AdfsAuthResult? GetCachedTokens();

        /// <summary>
        /// Clears the cached tokens.
        /// </summary>
        void ClearCachedTokens();

        /// <summary>
        /// Parses a JWT token and returns the claims.
        /// </summary>
        /// <param name="token">The JWT token to parse.</param>
        /// <returns>A dictionary of claims.</returns>
        IDictionary<string, string> ParseToken(string token);
    }
}
