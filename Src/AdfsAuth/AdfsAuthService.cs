using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IdentityModel.Tokens.Jwt;
using System.Timers;
using Timer = System.Timers.Timer;

namespace AdfsAuth
{
    /// <summary>
    /// Service for authenticating with ADFS using Node.js automation.
    /// </summary>
    public class AdfsAuthService : IAdfsAuthService, IDisposable
    {
        private readonly AdfsAuthOptions _options;
        private readonly ILogger<AdfsAuthService> _logger;
        private readonly string _nodeScriptPath;
        private AdfsAuthResult? _cachedTokens;
        private Timer? _refreshTimer;
        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="AdfsAuthService"/> class.
        /// </summary>
        /// <param name="options">The ADFS authentication options.</param>
        /// <param name="logger">The logger.</param>
        public AdfsAuthService(IOptions<AdfsAuthOptions> options, ILogger<AdfsAuthService> logger)
        {
            _options = options.Value;
            _logger = logger;
            
            // Determine the Node.js script path
            _nodeScriptPath = DetermineNodeScriptPath();
            
            // Install dependencies if needed
            if (_options.AutoInstallDependencies)
            {
                InstallNodeDependencies();
            }
            
            // Load cached tokens if enabled
            if (_options.CacheTokens && !string.IsNullOrEmpty(_options.TokenCacheFile))
            {
                LoadCachedTokens();
            }
        }

        /// <summary>
        /// Authenticates with ADFS using the configured options.
        /// </summary>
        /// <returns>The authentication result.</returns>
        public async Task<AdfsAuthResult> AuthenticateAsync()
        {
            return await AuthenticateAsync(_options);
        }

        /// <summary>
        /// Authenticates with ADFS using the specified options.
        /// </summary>
        /// <param name="options">The authentication options to use.</param>
        /// <returns>The authentication result.</returns>
        public async Task<AdfsAuthResult> AuthenticateAsync(AdfsAuthOptions options)
        {
            // Check if we have valid cached tokens
            if (_cachedTokens != null && IsTokenValid(_cachedTokens.AccessToken))
            {
                _logger.LogInformation("Using cached tokens");
                return _cachedTokens;
            }
            
            // Load credentials if needed
            await LoadCredentialsIfNeededAsync(options);
            
            _logger.LogInformation("Starting ADFS authentication with Node.js automation");
            
            try
            {
                // Create a temporary file to store the configuration
                var configFile = Path.GetTempFileName();
                var configJson = JsonSerializer.Serialize(options);
                await File.WriteAllTextAsync(configFile, configJson);
                
                // Run the Node.js script
                var result = await RunNodeScriptAsync(options);
                
                // Clean up the temporary file
                try
                {
                    File.Delete(configFile);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete temporary config file: {FilePath}", configFile);
                }
                
                // Cache the tokens if successful
                if (result.Success && options.CacheTokens)
                {
                    _cachedTokens = result;
                    SaveCachedTokens();
                    
                    // Set up token refresh if enabled
                    if (options.AutoRefreshTokens)
                    {
                        SetupTokenRefresh(result);
                    }
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during ADFS authentication");
                return new AdfsAuthResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        /// <summary>
        /// Gets the current cached tokens, or null if no tokens are cached.
        /// </summary>
        /// <returns>The cached tokens, or null if no tokens are cached.</returns>
        public AdfsAuthResult? GetCachedTokens()
        {
            return _cachedTokens;
        }

        /// <summary>
        /// Clears the cached tokens.
        /// </summary>
        public void ClearCachedTokens()
        {
            _cachedTokens = null;
            
            if (_options.CacheTokens && !string.IsNullOrEmpty(_options.TokenCacheFile) && File.Exists(_options.TokenCacheFile))
            {
                try
                {
                    File.Delete(_options.TokenCacheFile);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete token cache file: {FilePath}", _options.TokenCacheFile);
                }
            }
        }

        /// <summary>
        /// Parses a JWT token and returns the claims.
        /// </summary>
        /// <param name="token">The JWT token to parse.</param>
        /// <returns>A dictionary of claims.</returns>
        public IDictionary<string, string> ParseToken(string token)
        {
            var claims = new Dictionary<string, string>();
            
            try
            {
                var handler = new JwtSecurityTokenHandler();
                var jwtToken = handler.ReadJwtToken(token);
                
                foreach (var claim in jwtToken.Claims)
                {
                    claims[claim.Type] = claim.Value;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing JWT token");
            }
            
            return claims;
        }

        /// <summary>
        /// Disposes the service and cleans up resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        /// <summary>
        /// Disposes the service and cleans up resources.
        /// </summary>
        /// <param name="disposing">Whether to dispose managed resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources
                    if (_refreshTimer != null)
                    {
                        _refreshTimer.Dispose();
                        _refreshTimer = null;
                    }
                }
                
                _disposed = true;
            }
        }

        private string DetermineNodeScriptPath()
        {
            // If a custom path is specified, use that
            if (!string.IsNullOrEmpty(_options.NodeScriptPath))
            {
                return _options.NodeScriptPath;
            }
            
            // Check if the script is in the current directory
            var currentDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "node");
            if (Directory.Exists(currentDir))
            {
                return currentDir;
            }
            
            // Check if the script is in the package directory
            var packageDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "content", "node");
            if (Directory.Exists(packageDir))
            {
                return packageDir;
            }
            
            // Default to the current directory
            return currentDir;
        }

        private void InstallNodeDependencies()
        {
            try
            {
                _logger.LogInformation("Installing Node.js dependencies in {NodeScriptPath}", _nodeScriptPath);

                // Create the process to run npm install
                var startInfo = new ProcessStartInfo
                {
                    FileName = "/opt/homebrew/bin/npm",
                    Arguments = "install",
                    WorkingDirectory = _nodeScriptPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    _logger.LogError("Failed to start npm process");
                    return;
                }

                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    var error = process.StandardError.ReadToEnd();
                    _logger.LogError("npm install failed with exit code {ExitCode}: {Error}", process.ExitCode, error);
                    return;
                }

                _logger.LogInformation("Node.js dependencies installed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error installing Node.js dependencies");
            }
        }

        private async Task<AdfsAuthResult> RunNodeScriptAsync(AdfsAuthOptions options)
        {
            try
            {
                _logger.LogInformation("Starting ADFS authentication with Node.js automation");

                // Create a temporary configuration file for the Node.js script
                var configFilePath = Path.Combine(_nodeScriptPath, "config.json");
                
                // Create a dictionary for the config to avoid property name conflicts
                var configDict = new Dictionary<string, object>
                {
                    { "authority", options.Authority },
                    { "clientId", options.ClientId },
                    { "redirectUri", options.RedirectUri },
                    { "scope", options.Scope },
                    { "username", options.Username },
                    { "password", options.Password },
                    { "headless", options.Headless },
                    { "slowMo", options.SlowMo },
                    { "timeout", options.Timeout },
                    { "outputFile", options.OutputFile }
                };
                
                await File.WriteAllTextAsync(configFilePath, JsonSerializer.Serialize(configDict, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

                // Create the process to run the Node.js script
                var scriptPath = Path.Combine(_nodeScriptPath, "adfs-auth-cli.js");
                _logger.LogInformation("Running Node.js script: {ScriptPath}", scriptPath);

                var startInfo = new ProcessStartInfo
                {
                    FileName = "node",
                    Arguments = $"\"{scriptPath}\" \"{configFilePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = _nodeScriptPath
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    _logger.LogError("Failed to start Node.js process");
                    return new AdfsAuthResult
                    {
                        Success = false,
                        Error = "Failed to start Node.js process"
                    };
                }

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    _logger.LogError("Node.js script failed with exit code {ExitCode}: {Error}", process.ExitCode, error);
                    return new AdfsAuthResult
                    {
                        Success = false,
                        Error = $"Node.js script failed with exit code {process.ExitCode}",
                        ErrorDescription = error
                    };
                }

                // Parse the output
                try
                {
                    // Extract the JSON result from the output
                    // Look for the last occurrence of a JSON object in the output
                    var jsonStart = output.LastIndexOf('{');
                    var jsonEnd = output.LastIndexOf('}');
                    
                    if (jsonStart >= 0 && jsonEnd > jsonStart)
                    {
                        var jsonResult = output.Substring(jsonStart, jsonEnd - jsonStart + 1);
                        _logger.LogDebug($"Parsed JSON result: {jsonResult}");
                        return JsonSerializer.Deserialize<AdfsAuthResult>(jsonResult);
                    }
                    else
                    {
                        _logger.LogError($"Failed to find JSON result in output: {output}");
                        return new AdfsAuthResult
                        {
                            Success = false,
                            Error = "Failed to find JSON result in output",
                            ErrorDescription = output
                        };
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to parse Node.js script output: {Output}", output);
                    return new AdfsAuthResult
                    {
                        Success = false,
                        Error = "Failed to parse Node.js script output",
                        ErrorDescription = ex.Message
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running Node.js script");
                return new AdfsAuthResult
                {
                    Success = false,
                    Error = "Error running Node.js script",
                    ErrorDescription = ex.Message
                };
            }
        }
        
        private async Task LoadCredentialsIfNeededAsync(AdfsAuthOptions options)
        {
            // If username or password is already set, we don't need to load credentials
            if (!string.IsNullOrEmpty(options.Username) && !string.IsNullOrEmpty(options.Password))
            {
                return;
            }
            
            // Try to load credentials from environment variables
            if (options.LoadCredentialsFromEnvironment)
            {
                var username = Environment.GetEnvironmentVariable(options.UsernameEnvironmentVariable);
                var password = Environment.GetEnvironmentVariable(options.PasswordEnvironmentVariable);
                
                if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                {
                    _logger.LogInformation("Loaded credentials from environment variables");
                    options.Username = username;
                    options.Password = password;
                    return;
                }
            }
            
            // Try to load credentials from JSON file
            if (!string.IsNullOrEmpty(options.CredentialsJsonFile) && File.Exists(options.CredentialsJsonFile))
            {
                try
                {
                    var json = File.ReadAllText(options.CredentialsJsonFile);
                    var credentials = System.Text.Json.JsonSerializer.Deserialize<JsonCredentials>(json);
                    
                    if (credentials != null)
                    {
                        // Handle both uppercase and lowercase property names
                        var username = credentials.username ?? credentials.Username;
                        var password = credentials.password ?? credentials.Password;
                        
                        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                        {
                            _logger.LogInformation("Loaded credentials from JSON file: {CredentialsJsonFile}", options.CredentialsJsonFile);
                            options.Username = username;
                            options.Password = password;
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load credentials from JSON file: {CredentialsJsonFile}", options.CredentialsJsonFile);
                }
            }
            
            // Try to load credentials from file
            if (!string.IsNullOrEmpty(options.CredentialsFile) && File.Exists(options.CredentialsFile))
            {
                try
                {
                    var lines = File.ReadAllLines(options.CredentialsFile);
                    
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("username="))
                        {
                            options.Username = line.Substring("username=".Length).Trim();
                        }
                        else if (line.StartsWith("password="))
                        {
                            options.Password = line.Substring("password=".Length).Trim();
                        }
                    }
                    
                    if (!string.IsNullOrEmpty(options.Username) && !string.IsNullOrEmpty(options.Password))
                    {
                        _logger.LogInformation("Loaded credentials from file");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load credentials from file");
                }
            }
            
            // If we still don't have credentials, log a warning
            if (string.IsNullOrEmpty(options.Username) || string.IsNullOrEmpty(options.Password))
            {
                _logger.LogWarning("No credentials provided. Authentication will likely fail.");
            }
        }
        
        private void LoadCachedTokens()
        {
            if (string.IsNullOrEmpty(_options.TokenCacheFile))
            {
                return;
            }
            
            try
            {
                if (File.Exists(_options.TokenCacheFile))
                {
                    _logger.LogInformation("Loading cached tokens from {FilePath}", _options.TokenCacheFile);
                    
                    var json = File.ReadAllText(_options.TokenCacheFile);
                    var cachedTokens = JsonSerializer.Deserialize<AdfsAuthResult>(json);
                    
                    if (cachedTokens != null && IsTokenValid(cachedTokens.AccessToken))
                    {
                        _cachedTokens = cachedTokens;
                        _logger.LogInformation("Loaded valid cached tokens");
                        
                        // Set up token refresh if enabled
                        if (_options.AutoRefreshTokens)
                        {
                            SetupTokenRefresh(cachedTokens);
                        }
                    }
                    else
                    {
                        _logger.LogInformation("Cached tokens are invalid or expired");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading cached tokens");
            }
        }
        
        private void SaveCachedTokens()
        {
            if (_cachedTokens == null || string.IsNullOrEmpty(_options.TokenCacheFile))
            {
                return;
            }
            
            try
            {
                _logger.LogInformation("Saving tokens to cache: {FilePath}", _options.TokenCacheFile);
                
                var json = JsonSerializer.Serialize(_cachedTokens);
                File.WriteAllText(_options.TokenCacheFile, json);
                
                _logger.LogInformation("Tokens saved to cache");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving tokens to cache");
            }
        }
        
        private bool IsTokenValid(string? token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }
            
            try
            {
                var handler = new JwtSecurityTokenHandler();
                var jwtToken = handler.ReadJwtToken(token);
                
                // Check if the token is expired
                return jwtToken.ValidTo > DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating token");
                return false;
            }
        }
        
        private void SetupTokenRefresh(AdfsAuthResult tokens)
        {
            if (!_options.AutoRefreshTokens || string.IsNullOrEmpty(tokens.AccessToken))
            {
                return;
            }
            
            try
            {
                var handler = new JwtSecurityTokenHandler();
                var jwtToken = handler.ReadJwtToken(tokens.AccessToken);
                
                // Calculate when to refresh the token
                var expiresAt = jwtToken.ValidTo;
                var refreshAt = expiresAt.AddMinutes(-_options.RefreshBeforeExpirationMinutes);
                var now = DateTime.UtcNow;
                
                if (refreshAt <= now)
                {
                    // Token is already due for refresh
                    _logger.LogInformation("Token is already due for refresh, refreshing now");
                    _ = AuthenticateAsync();
                    return;
                }
                
                // Calculate the delay until refresh
                var delay = refreshAt - now;
                
                _logger.LogInformation("Setting up token refresh in {Delay}", delay);
                
                // Dispose of any existing timer
                _refreshTimer?.Dispose();
                
                // Create a new timer
                _refreshTimer = new Timer(delay.TotalMilliseconds);
                _refreshTimer.Elapsed += async (sender, e) => await RefreshTokensAsync();
                _refreshTimer.AutoReset = false;
                _refreshTimer.Start();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting up token refresh");
            }
        }
        
        private async Task RefreshTokensAsync()
        {
            _logger.LogInformation("Refreshing tokens");
            
            try
            {
                var result = await AuthenticateAsync();
                
                if (result.Success)
                {
                    _logger.LogInformation("Tokens refreshed successfully");
                }
                else
                {
                    _logger.LogError("Failed to refresh tokens: {Error}", result.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing tokens");
            }
        }

        private class JsonCredentials
        {
            public string? username { get; set; }
            public string? Username { get; set; }
            public string? password { get; set; }
            public string? Password { get; set; }
        }
    }
}
