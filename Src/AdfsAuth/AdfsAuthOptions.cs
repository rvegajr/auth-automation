using System.Text.Json.Serialization;

namespace AdfsAuth
{
    /// <summary>
    /// Configuration options for ADFS authentication.
    /// </summary>
    public class AdfsAuthOptions
    {
        /// <summary>
        /// The ADFS authority URL (e.g., https://adfs.example.com).
        /// </summary>
        public string? Authority { get; set; } = "https://auth.integ.alliedpilots.org";

        /// <summary>
        /// The client ID registered with ADFS.
        /// </summary>
        public string? ClientId { get; set; } = "https://sentinel.alliedpilots.org";

        /// <summary>
        /// The redirect URI registered with ADFS.
        /// </summary>
        public string? RedirectUri { get; set; } = "https://sentinel.integ.alliedpilots.org/redirect";

        /// <summary>
        /// The scope to request (space-separated).
        /// </summary>
        public string? Scope { get; set; } = "openid offline_access";

        /// <summary>
        /// The username for authentication.
        /// </summary>
        public string? Username { get; set; }

        /// <summary>
        /// The password for authentication.
        /// </summary>
        public string? Password { get; set; }

        /// <summary>
        /// Path to a file containing credentials (username=xxx\npassword=yyy).
        /// </summary>
        public string? CredentialsFile { get; set; }
        
        /// <summary>
        /// Path to a JSON file containing credentials with Username and Password properties.
        /// </summary>
        public string? CredentialsJsonFile { get; set; }

        /// <summary>
        /// The environment variable to use for the username.
        /// </summary>
        public string UsernameEnvironmentVariable { get; set; } = "ADFS_USERNAME";

        /// <summary>
        /// The environment variable to use for the password.
        /// </summary>
        public string PasswordEnvironmentVariable { get; set; } = "ADFS_PASSWORD";

        /// <summary>
        /// Whether to run the browser in headless mode.
        /// </summary>
        public bool Headless { get; set; } = true;

        /// <summary>
        /// Slow down browser operations by specified milliseconds.
        /// </summary>
        public int SlowMo { get; set; } = 50;

        /// <summary>
        /// Timeout for authentication in milliseconds.
        /// </summary>
        public int Timeout { get; set; } = 60000;

        /// <summary>
        /// Path to save the tokens to.
        /// </summary>
        public string? OutputFile { get; set; }

        /// <summary>
        /// Path to the Node.js executable.
        /// </summary>
        public string NodePath { get; set; } = "node";

        /// <summary>
        /// Path to the Node.js script directory.
        /// </summary>
        public string? NodeScriptPath { get; set; }

        /// <summary>
        /// Whether to install Node.js dependencies automatically.
        /// </summary>
        public bool AutoInstallDependencies { get; set; } = true;

        /// <summary>
        /// Whether to automatically refresh tokens before they expire.
        /// </summary>
        public bool AutoRefreshTokens { get; set; } = false;

        /// <summary>
        /// The time in minutes before token expiration to trigger a refresh.
        /// </summary>
        public int RefreshBeforeExpirationMinutes { get; set; } = 5;

        /// <summary>
        /// Whether to cache tokens to disk for reuse.
        /// </summary>
        public bool CacheTokens { get; set; } = false;

        /// <summary>
        /// Path to the token cache file.
        /// </summary>
        public string? TokenCacheFile { get; set; } = "adfs_token_cache.json";

        /// <summary>
        /// Whether to automatically load credentials from environment variables.
        /// </summary>
        public bool LoadCredentialsFromEnvironment { get; set; } = false;

        /// <summary>
        /// Configuration for taking screenshots during authentication.
        /// </summary>
        public ScreenshotsOptions? Screenshots { get; set; }
    }

    /// <summary>
    /// Options for configuring screenshots during authentication.
    /// </summary>
    public class ScreenshotsOptions
    {
        /// <summary>
        /// Whether to enable screenshots.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// The directory to save screenshots to.
        /// </summary>
        public string? Directory { get; set; } = "screenshots";

        /// <summary>
        /// The prefix to use for screenshot filenames.
        /// </summary>
        public string? Prefix { get; set; } = "auth_";
    }

    /// <summary>
    /// Result of an ADFS authentication attempt.
    /// </summary>
    public class AdfsAuthResult
    {
        /// <summary>
        /// Whether the authentication was successful.
        /// </summary>
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        /// <summary>
        /// The access token, if authentication was successful.
        /// </summary>
        [JsonPropertyName("accessToken")]
        public string? AccessToken { get; set; }

        /// <summary>
        /// The ID token, if provided.
        /// </summary>
        [JsonPropertyName("idToken")]
        public string? IdToken { get; set; }
        
        /// <summary>
        /// The final URL after all redirects.
        /// </summary>
        [JsonPropertyName("finalUrl")]
        public string? FinalUrl { get; set; }
        
        /// <summary>
        /// Tokens returned from the authentication process.
        /// </summary>
        [JsonPropertyName("tokens")]
        public TokensResult? Tokens { get; set; }

        /// <summary>
        /// The error message, if authentication failed.
        /// </summary>
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        /// <summary>
        /// The error description, if provided.
        /// </summary>
        [JsonPropertyName("errorDescription")]
        public string? ErrorDescription { get; set; }

        /// <summary>
        /// The timestamp of the authentication.
        /// </summary>
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
    
    /// <summary>
    /// Represents the tokens returned from the authentication process.
    /// </summary>
    public class TokensResult
    {
        /// <summary>
        /// The access token for API calls.
        /// </summary>
        [JsonPropertyName("accessToken")]
        public string? AccessToken { get; set; }
        
        /// <summary>
        /// The ID token containing user claims.
        /// </summary>
        [JsonPropertyName("idToken")]
        public string? IdToken { get; set; }
        
        /// <summary>
        /// The token type (usually "Bearer").
        /// </summary>
        [JsonPropertyName("tokenType")]
        public string? TokenType { get; set; }
        
        /// <summary>
        /// The token expiration time in seconds.
        /// </summary>
        [JsonPropertyName("expiresIn")]
        public string? ExpiresIn { get; set; }
        
        /// <summary>
        /// The scopes granted by the token.
        /// </summary>
        [JsonPropertyName("scope")]
        public string? Scope { get; set; }
        
        /// <summary>
        /// The state parameter used in the authentication request.
        /// </summary>
        [JsonPropertyName("state")]
        public string? State { get; set; }
    }
}
