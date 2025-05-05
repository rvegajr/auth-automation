using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AdfsAuth;
using System.Collections.Generic;
using System.Linq;
using System.IdentityModel.Tokens.Jwt;

namespace AdfsAutomationUsage
{
    class Program
    {
        // Global verbose flag
        private static bool _verbose = false;
        private static IConfiguration _configuration;
        
        static async Task<int> Main(string[] args)
        {
            try
            {
                Console.WriteLine("=== ADFS Authentication CLI ===");
                
                // Build configuration
                _configuration = new ConfigurationBuilder()
                    .SetBasePath(GetConfigurationDirectory())
                    .AddJsonFile("appsettings.json", optional: true)
                    .Build();
                
                // Check for parameters file in the current directory
                CheckAndCreateParametersFile();
                
                // Build command line interface
                var rootCommand = BuildCommandLineInterface();
                
                // Parse and execute command
                return await rootCommand.InvokeAsync(args);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ An error occurred: {ex.Message}");
                if (_verbose)
                {
                    Console.WriteLine(ex.StackTrace);
                }
                return 1;
            }
        }
        
        private static string GetConfigurationDirectory()
        {
            // First, try the current directory
            string currentDir = Directory.GetCurrentDirectory();
            if (File.Exists(Path.Combine(currentDir, "appsettings.json")))
            {
                return currentDir;
            }
            
            // Next, try the executable directory
            string executableDir = AppContext.BaseDirectory;
            if (File.Exists(Path.Combine(executableDir, "appsettings.json")))
            {
                return executableDir;
            }
            
            // Finally, check if there's a config in the user's home directory
            string userConfigDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
                ".adfs-auth");
                
            if (!Directory.Exists(userConfigDir))
            {
                Directory.CreateDirectory(userConfigDir);
                
                // If no config exists in the user directory, create a default one
                if (!File.Exists(Path.Combine(userConfigDir, "appsettings.json")))
                {
                    CreateDefaultConfig(userConfigDir);
                }
            }
            
            return userConfigDir;
        }
        
        private static void CreateDefaultConfig(string configDir)
        {
            string configPath = Path.Combine(configDir, "appsettings.json");
            string defaultConfig = @"{
  ""AdfsAuth"": {
    ""Authority"": ""https://adfs.example.com"",
    ""ClientId"": ""your-client-id"",
    ""RedirectUri"": ""your-redirect-uri"",
    ""Scope"": ""openid offline_access"",
    ""OutputFile"": ""tokens.json"",
    ""Headless"": true,
    ""SlowMo"": 50,
    ""Timeout"": 300000,
    ""CredentialsJsonFile"": "".parameters"",
    ""Screenshots"": {
      ""Enabled"": true,
      ""Directory"": ""screenshots"",
      ""Prefix"": ""auth_""
    },
    ""TokenCacheFile"": ""adfs_token_cache.json"",
    ""AutoInstallDependencies"": true
  }
}";
            File.WriteAllText(configPath, defaultConfig);
            Console.WriteLine($"Created default configuration at {configPath}");
            Console.WriteLine("Please edit this file with your ADFS settings before using the tool.");
        }
        
        private static void CheckAndCreateParametersFile()
        {
            try
            {
                // Check if .parameters file exists in the current directory
                var currentDir = Directory.GetCurrentDirectory();
                var parametersFile = Path.Combine(currentDir, ".parameters");
                
                if (!File.Exists(parametersFile))
                {
                    // Check if parameters-template.json exists in the tool directory
                    var templateFile = Path.Combine(AppContext.BaseDirectory, "parameters-template.json");
                    
                    if (File.Exists(templateFile))
                    {
                        Console.WriteLine("No parameters file found in the current directory.");
                        Console.WriteLine("Creating a template parameters file (.parameters) in the current directory...");
                        
                        // Copy the template file to the current directory as .parameters
                        File.Copy(templateFile, parametersFile);
                        
                        Console.WriteLine($"✅ Parameters file created: {parametersFile}");
                        Console.WriteLine("Please edit this file with your ADFS credentials and configuration.");
                    }
                    else
                    {
                        Console.WriteLine("Warning: Template parameters file not found in the tool directory.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not create parameters file: {ex.Message}");
            }
        }
        
        private static RootCommand BuildCommandLineInterface()
        {
            // Create a root command with options
            var rootCommand = new RootCommand("ADFS Authentication CLI tool");
            
            // Add global verbose option
            var verboseOption = new Option<bool>(
                new[] { "--verbose", "-v" },
                "Enable verbose output for detailed debugging information"
            );
            rootCommand.AddGlobalOption(verboseOption);
            
            // Add authenticate command
            var authenticateCommand = new Command("authenticate", "Authenticate with ADFS and get tokens");
            authenticateCommand.AddOption(verboseOption);
            
            // Add parameters file option
            var parametersFileOption = new Option<string>(
                new[] { "--parameters-file", "-p" },
                "Path to a custom parameters file containing credentials and configuration"
            );
            authenticateCommand.AddOption(parametersFileOption);
            
            authenticateCommand.SetHandler(async (InvocationContext context) => 
            {
                _verbose = context.ParseResult.GetValueForOption(verboseOption);
                var customParametersFile = context.ParseResult.GetValueForOption(parametersFileOption);
                context.ExitCode = await Authenticate(customParametersFile);
            });
            rootCommand.Add(authenticateCommand);
            
            // Add clear-cache command
            var clearCacheCommand = new Command("clear-cache", "Clear cached tokens");
            clearCacheCommand.AddOption(verboseOption);
            clearCacheCommand.SetHandler((InvocationContext context) => 
            {
                _verbose = context.ParseResult.GetValueForOption(verboseOption);
                context.ExitCode = ClearCache();
            });
            rootCommand.Add(clearCacheCommand);
            
            // Add show-tokens command
            var showTokensCommand = new Command("show-tokens", "Show cached tokens if available");
            showTokensCommand.AddOption(verboseOption);
            showTokensCommand.SetHandler((InvocationContext context) => 
            {
                _verbose = context.ParseResult.GetValueForOption(verboseOption);
                context.ExitCode = ShowCachedTokens();
            });
            rootCommand.Add(showTokensCommand);
            
            // Add show-credentials command
            var showCredentialsCommand = new Command("show-credentials", "Show the credentials that will be used for authentication");
            showCredentialsCommand.AddOption(verboseOption);
            showCredentialsCommand.AddOption(parametersFileOption);
            showCredentialsCommand.SetHandler((InvocationContext context) => 
            {
                _verbose = context.ParseResult.GetValueForOption(verboseOption);
                var customParametersFile = context.ParseResult.GetValueForOption(parametersFileOption);
                context.ExitCode = ShowCredentialsConfiguration(customParametersFile);
            });
            rootCommand.Add(showCredentialsCommand);
            
            // Add show-token-file command
            var showTokenFileCommand = new Command("show-token-file", "Show tokens from a specific file");
            showTokenFileCommand.AddOption(verboseOption);
            var tokenFileOption = new Option<string>(
                "--file", 
                "Path to the token file") { IsRequired = true };
            showTokenFileCommand.AddOption(tokenFileOption);
            showTokenFileCommand.SetHandler((InvocationContext context) => 
            {
                _verbose = context.ParseResult.GetValueForOption(verboseOption);
                var filePath = context.ParseResult.GetValueForOption(tokenFileOption);
                context.ExitCode = ShowTokenFile(filePath);
            });
            rootCommand.Add(showTokenFileCommand);
            
            return rootCommand;
        }
        
        private static async Task<int> Authenticate(string customParametersFile = null)
        {
            Console.WriteLine("Authenticating with ADFS...");

            try
            {
                LogVerbose("Starting authentication process...");

                // Get the service with the custom parameters file
                var adfsAuthService = GetAdfsAuthService(customParametersFile);

                // Authenticate
                var result = await adfsAuthService.AuthenticateAsync();

                if (result.Success)
                {
                    Console.WriteLine("✅ Authentication successful!");

                    if (!string.IsNullOrEmpty(result.AccessToken))
                    {
                        Console.WriteLine($"Access Token: {result.AccessToken[..20]}...");
                    }

                    if (!string.IsNullOrEmpty(result.IdToken))
                    {
                        Console.WriteLine($"ID Token: {result.IdToken[..20]}...");
                    }

                    return 0;
                }
                else
                {
                    Console.WriteLine("❌ Authentication failed!");
                    Console.WriteLine($"Error: {result.Error}");
                    Console.WriteLine($"Description: {result.ErrorDescription}");
                    return 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Authentication failed with exception: {ex.Message}");
                if (_verbose)
                {
                    Console.WriteLine(ex.StackTrace);
                }
                return 1;
            }
        }
        
        private static IAdfsAuthService GetAdfsAuthService(string customParametersFile = null)
        {
            // Create service collection
            LogVerbose("Creating service collection...");
            var services = new ServiceCollection();
            
            // Add logging
            LogVerbose("Configuring logging...");
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(_verbose ? LogLevel.Debug : LogLevel.Information);
            });
            
            // Add ADFS authentication service
            LogVerbose("Adding ADFS authentication service...");
            services.Configure<AdfsAuthOptions>(options => 
            {
                // First try to load the base configuration from appsettings.json if available
                var adfsSection = _configuration.GetSection("AdfsAuth");
                if (adfsSection.Exists())
                {
                    LogVerbose("Loading configuration from appsettings.json...");
                    adfsSection.Bind(options);
                }
                else
                {
                    LogVerbose("No appsettings.json found or no AdfsAuth section exists.");
                    
                    // Set some reasonable defaults if no configuration exists
                    options.Headless = true;
                    options.AutoInstallDependencies = true;
                    options.CacheTokens = true;
                    options.AutoRefreshTokens = true;
                    options.LoadCredentialsFromEnvironment = true;
                    options.OutputFile = "tokens.json";
                    options.TokenCacheFile = "adfs_token_cache.json";
                    
                    if (_verbose)
                    {
                        LogVerbose("Setting verbose debug options...");
                        options.SlowMo = 200; // Slow down automation for better visibility
                    }
                }
                
                // If a custom parameters file is provided, override with those settings
                if (!string.IsNullOrEmpty(customParametersFile))
                {
                    // Convert to absolute path if it's a relative path
                    if (!Path.IsPathRooted(customParametersFile))
                    {
                        customParametersFile = Path.GetFullPath(customParametersFile);
                    }
                    
                    if (!File.Exists(customParametersFile))
                    {
                        Console.WriteLine($"Warning: Custom parameters file not found: {customParametersFile}");
                    }
                    else
                    {
                        LogVerbose($"Loading custom parameters from: {customParametersFile}");
                        try
                        {
                            // Read the file content
                            var fileContent = File.ReadAllText(customParametersFile);
                            
                            // Check if it's a JSON file
                            if (customParametersFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                                fileContent.TrimStart().StartsWith("{"))
                            {
                                // Parse as JSON
                                var customConfig = new ConfigurationBuilder()
                                    .AddJsonFile(customParametersFile, optional: false)
                                    .Build();
                                
                                // Check if it has an AdfsAuth section
                                if (customConfig.GetSection("AdfsAuth").Exists())
                                {
                                    customConfig.GetSection("AdfsAuth").Bind(options);
                                    LogVerbose("Loaded AdfsAuth section from custom parameters file");
                                }
                                else
                                {
                                    // If no AdfsAuth section, bind the root
                                    customConfig.Bind(options);
                                    LogVerbose("Loaded root section from custom parameters file");
                                }
                                
                                // Set the credentials file to the custom parameters file
                                options.CredentialsJsonFile = customParametersFile;
                                LogVerbose($"Set CredentialsJsonFile to: {customParametersFile}");
                            }
                            else
                            {
                                // Assume it's a credentials file
                                options.CredentialsJsonFile = customParametersFile;
                                LogVerbose($"Using custom parameters file as credentials file: {customParametersFile}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error loading custom parameters file: {ex.Message}");
                            if (_verbose)
                            {
                                Console.WriteLine(ex.StackTrace);
                            }
                        }
                    }
                }
                
                // Validate required configuration
                if (string.IsNullOrEmpty(options.Authority) && !string.IsNullOrEmpty(customParametersFile))
                {
                    Console.WriteLine("Warning: Authority URL is not set. Make sure your parameters file includes the Authority URL.");
                }
                
                if (string.IsNullOrEmpty(options.ClientId) && !string.IsNullOrEmpty(customParametersFile))
                {
                    Console.WriteLine("Warning: Client ID is not set. Make sure your parameters file includes the Client ID.");
                }
                
                if (string.IsNullOrEmpty(options.RedirectUri) && !string.IsNullOrEmpty(customParametersFile))
                {
                    Console.WriteLine("Warning: Redirect URI is not set. Make sure your parameters file includes the Redirect URI.");
                }
            });
            
            // Replace the default AdfsAuthService with our enhanced version
            services.AddTransient<IAdfsAuthService>(provider => 
            {
                var options = provider.GetRequiredService<IOptions<AdfsAuthOptions>>();
                var logger = provider.GetRequiredService<ILogger<EnhancedAdfsAuthService>>();
                return new EnhancedAdfsAuthService(options, logger, _verbose);
            });
            
            // Build service provider
            LogVerbose("Building service provider...");
            var serviceProvider = services.BuildServiceProvider();
            
            // Get the service
            LogVerbose("Resolving IAdfsAuthService...");
            return serviceProvider.GetRequiredService<IAdfsAuthService>();
        }
        
        private static int ClearCache()
        {
            try
            {
                Console.WriteLine("Clearing cached tokens...");
                
                // Get the service
                var adfsAuthService = GetAdfsAuthService();
                
                // Clear cached tokens
                adfsAuthService.ClearCachedTokens();
                
                Console.WriteLine("✅ Token cache cleared successfully!");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error clearing token cache: {ex.Message}");
                if (_verbose)
                {
                    Console.WriteLine(ex.StackTrace);
                }
                return 1;
            }
        }
        
        private static int ShowCachedTokens()
        {
            try
            {
                Console.WriteLine("Loading cached tokens...");
                
                // Get the service
                var adfsAuthService = GetAdfsAuthService();
                
                // Get cached tokens
                var cachedTokens = adfsAuthService.GetCachedTokens();
                
                if (cachedTokens != null && cachedTokens.Success)
                {
                    Console.WriteLine("✅ Cached tokens found!");
                    
                    if (!string.IsNullOrEmpty(cachedTokens.AccessToken))
                    {
                        Console.WriteLine($"Access Token: {cachedTokens.AccessToken[..20]}...");
                        
                        // Parse token to get expiration
                        var handler = new JwtSecurityTokenHandler();
                        var token = handler.ReadJwtToken(cachedTokens.AccessToken);
                        
                        Console.WriteLine($"Expires: {token.ValidTo}");
                        Console.WriteLine($"Valid: {token.ValidTo > DateTime.UtcNow}");
                        
                        if (_verbose)
                        {
                            Console.WriteLine("\nToken claims:");
                            foreach (var claim in token.Claims)
                            {
                                Console.WriteLine($"  {claim.Type}: {claim.Value}");
                            }
                        }
                    }
                    
                    if (!string.IsNullOrEmpty(cachedTokens.IdToken))
                    {
                        Console.WriteLine($"\nID Token: {cachedTokens.IdToken[..20]}...");
                    }
                    
                    return 0;
                }
                else
                {
                    Console.WriteLine("❌ No cached tokens found.");
                    return 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error loading cached tokens: {ex.Message}");
                if (_verbose)
                {
                    Console.WriteLine(ex.StackTrace);
                }
                return 1;
            }
        }
        
        private static int ShowCredentialsConfiguration(string customParametersFile = null)
        {
            try
            {
                Console.WriteLine("Loading credentials configuration...");
                
                // Get configuration from appsettings.json if available
                var adfsSection = _configuration.GetSection("AdfsAuth");
                bool hasAppSettings = adfsSection.Exists() && !string.IsNullOrEmpty(adfsSection["Authority"]);
                
                // If custom parameters file is provided, load it
                IConfigurationSection customAdfsSection = null;
                if (!string.IsNullOrEmpty(customParametersFile))
                {
                    // Convert to absolute path if it's a relative path
                    if (!Path.IsPathRooted(customParametersFile))
                    {
                        customParametersFile = Path.GetFullPath(customParametersFile);
                    }
                    
                    if (File.Exists(customParametersFile))
                    {
                        Console.WriteLine($"Loading custom parameters from: {customParametersFile}");
                        try
                        {
                            var customConfig = new ConfigurationBuilder()
                                .AddJsonFile(customParametersFile, optional: false)
                                .Build();
                                
                            // Check if it has an AdfsAuth section
                            if (customConfig.GetSection("AdfsAuth").Exists())
                            {
                                customAdfsSection = customConfig.GetSection("AdfsAuth");
                                Console.WriteLine("Using AdfsAuth section from custom parameters file");
                            }
                            else
                            {
                                // If no AdfsAuth section, use the root
                                customAdfsSection = customConfig.GetSection("");
                                Console.WriteLine("Using root section from custom parameters file");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error loading custom parameters file: {ex.Message}");
                            if (_verbose)
                            {
                                Console.WriteLine(ex.StackTrace);
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Warning: Custom parameters file not found: {customParametersFile}");
                    }
                }
                
                // Use custom section if available, otherwise use default
                var effectiveSection = customAdfsSection ?? adfsSection;
                
                Console.WriteLine("\nADFS Configuration:");
                
                // If we have no configuration at all, show a message
                if ((customAdfsSection == null && !hasAppSettings) || effectiveSection == null)
                {
                    Console.WriteLine("No configuration found. Please provide a parameters file or create an appsettings.json file.");
                    return 1;
                }
                
                // Display configuration values
                string authority = effectiveSection["Authority"];
                string clientId = effectiveSection["ClientId"];
                string redirectUri = effectiveSection["RedirectUri"];
                string scope = effectiveSection["Scope"];
                string headless = effectiveSection["Headless"];
                
                // If the values are null in the configuration section but exist in the parameters file,
                // try to get them directly from the parameters file
                if (string.IsNullOrEmpty(authority) && customAdfsSection != null)
                {
                    try
                    {
                        var fileContent = File.ReadAllText(customParametersFile);
                        var credentials = JsonSerializer.Deserialize<Dictionary<string, string>>(fileContent);
                        
                        if (credentials != null)
                        {
                            if (credentials.TryGetValue("Authority", out var auth) || credentials.TryGetValue("authority", out auth))
                            {
                                authority = auth;
                            }
                            
                            if (credentials.TryGetValue("ClientId", out var client) || credentials.TryGetValue("clientId", out client))
                            {
                                clientId = client;
                            }
                            
                            if (credentials.TryGetValue("RedirectUri", out var redirect) || credentials.TryGetValue("redirectUri", out redirect))
                            {
                                redirectUri = redirect;
                            }
                            
                            if (credentials.TryGetValue("Scope", out var s) || credentials.TryGetValue("scope", out s))
                            {
                                scope = s;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogVerbose($"Error parsing parameters file for configuration values: {ex.Message}");
                    }
                }
                
                Console.WriteLine($"Authority: {authority}");
                Console.WriteLine($"ClientId: {clientId}");
                Console.WriteLine($"RedirectUri: {redirectUri}");
                Console.WriteLine($"Scope: {scope}");
                Console.WriteLine($"Headless: {headless}");
                
                // Determine credentials file
                string credentialsFile;
                if (customAdfsSection != null && !string.IsNullOrEmpty(customParametersFile))
                {
                    // If custom parameters file is provided and loaded successfully, use it as credentials file
                    credentialsFile = customParametersFile;
                    Console.WriteLine($"CredentialsJsonFile: {credentialsFile} (custom)");
                }
                else
                {
                    // Otherwise use the one from configuration
                    credentialsFile = effectiveSection["CredentialsJsonFile"];
                    Console.WriteLine($"CredentialsJsonFile: {credentialsFile}");
                }
                
                // Try to load credentials from file
                if (!string.IsNullOrEmpty(credentialsFile))
                {
                    // Resolve relative path if needed
                    if (!Path.IsPathRooted(credentialsFile))
                    {
                        credentialsFile = Path.Combine(Directory.GetCurrentDirectory(), credentialsFile);
                    }
                    
                    Console.WriteLine($"\nAttempting to load credentials from: {credentialsFile}");
                    
                    if (File.Exists(credentialsFile))
                    {
                        LogVerbose($"Reading file: {credentialsFile}");
                        var jsonContent = File.ReadAllText(credentialsFile);
                        LogVerbose($"JSON content length: {jsonContent.Length} characters");
                        LogVerbose("Deserializing JSON content...");
                        
                        try
                        {
                            var credentials = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonContent);
                            
                            if (credentials != null)
                            {
                                Console.WriteLine("✅ Credentials loaded successfully!");
                                
                                if (credentials.TryGetValue("Username", out var username) || 
                                    credentials.TryGetValue("username", out username))
                                {
                                    Console.WriteLine($"Username: {username}");
                                }
                                
                                if (credentials.TryGetValue("Password", out var password) || 
                                    credentials.TryGetValue("password", out password))
                                {
                                    // Mask password for security
                                    var maskedPassword = password.Length > 4 
                                        ? $"{password[..2]}{'*' * (password.Length - 4)}{password[(password.Length - 2)..]}"
                                        : "****";
                                    Console.WriteLine($"Password: {maskedPassword}");
                                }
                                
                                // Display other configuration values if present
                                foreach (var key in credentials.Keys)
                                {
                                    if (key != "Username" && key != "username" && 
                                        key != "Password" && key != "password")
                                    {
                                        Console.WriteLine($"{key}: {credentials[key]}");
                                    }
                                }
                            }
                            else
                            {
                                Console.WriteLine("❌ Failed to parse credentials file.");
                            }
                        }
                        catch (JsonException ex)
                        {
                            Console.WriteLine($"❌ Error parsing credentials file: {ex.Message}");
                            if (_verbose)
                            {
                                Console.WriteLine(ex.StackTrace);
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"❌ Credentials file not found: {credentialsFile}");
                    }
                }
                
                // Check environment variables
                Console.WriteLine("\nChecking environment variables:");
                LogVerbose("Looking for ADFS_USERNAME and ADFS_PASSWORD environment variables...");
                
                var usernameEnv = Environment.GetEnvironmentVariable("ADFS_USERNAME");
                var passwordEnv = Environment.GetEnvironmentVariable("ADFS_PASSWORD");
                
                if (!string.IsNullOrEmpty(usernameEnv))
                {
                    Console.WriteLine("✅ ADFS_USERNAME environment variable found");
                }
                else
                {
                    Console.WriteLine("❌ ADFS_USERNAME environment variable not found");
                }
                
                if (!string.IsNullOrEmpty(passwordEnv))
                {
                    Console.WriteLine("✅ ADFS_PASSWORD environment variable found");
                }
                else
                {
                    Console.WriteLine("❌ ADFS_PASSWORD environment variable not found");
                }
                
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error loading credentials configuration: {ex.Message}");
                if (_verbose)
                {
                    Console.WriteLine(ex.StackTrace);
                }
                return 1;
            }
        }
        
        private static int ShowTokenFile(string filePath)
        {
            Console.WriteLine($"Loading tokens from file: {filePath}");
            
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"❌ Token file not found: {filePath}");
                return 1;
            }
            
            try
            {
                var json = File.ReadAllText(filePath);
                var tokens = JsonSerializer.Deserialize<JsonDocument>(json);
                
                if (tokens == null)
                {
                    Console.WriteLine("❌ Failed to parse token file");
                    return 1;
                }
                
                Console.WriteLine("✅ Tokens loaded successfully!");
                Console.WriteLine();
                Console.WriteLine("Token File Contents:");
                
                // Pretty print the JSON
                var options = new JsonSerializerOptions { WriteIndented = true };
                Console.WriteLine(JsonSerializer.Serialize(tokens, options));
                
                // Extract and display specific tokens if available
                if (tokens.RootElement.TryGetProperty("accessToken", out var accessToken))
                {
                    Console.WriteLine();
                    Console.WriteLine("Access Token:");
                    Console.WriteLine(accessToken.GetString());
                }
                
                if (tokens.RootElement.TryGetProperty("idToken", out var idToken))
                {
                    Console.WriteLine();
                    Console.WriteLine("ID Token:");
                    Console.WriteLine(idToken.GetString());
                }
                
                // Try to parse JWT tokens if present
                JsonElement accessTokenValue = default;
                JsonElement idTokenValue = default;
                bool hasAccessToken = tokens.RootElement.TryGetProperty("accessToken", out accessTokenValue);
                bool hasIdToken = tokens.RootElement.TryGetProperty("idToken", out idTokenValue);
                
                if (hasAccessToken || hasIdToken)
                {
                    Console.WriteLine();
                    Console.WriteLine("Token Information:");
                    
                    string tokenToParse = hasAccessToken 
                        ? accessTokenValue.GetString() 
                        : idTokenValue.GetString();
                    
                    if (!string.IsNullOrEmpty(tokenToParse))
                    {
                        try
                        {
                            var handler = new JwtSecurityTokenHandler();
                            var jwtToken = handler.ReadJwtToken(tokenToParse);
                            
                            Console.WriteLine($"Issuer: {jwtToken.Issuer}");
                            Console.WriteLine($"Audience: {jwtToken.Audiences.FirstOrDefault()}");
                            Console.WriteLine($"Valid From: {jwtToken.ValidFrom}");
                            Console.WriteLine($"Valid To: {jwtToken.ValidTo}");
                            Console.WriteLine($"Subject: {jwtToken.Subject}");
                            
                            Console.WriteLine();
                            Console.WriteLine("Claims:");
                            foreach (var claim in jwtToken.Claims)
                            {
                                Console.WriteLine($"  {claim.Type}: {claim.Value}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to parse JWT token: {ex.Message}");
                        }
                    }
                }
                
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error reading token file: {ex.Message}");
                if (_verbose)
                {
                    Console.WriteLine(ex.StackTrace);
                }
                return 1;
            }
        }
        
        private static void DisplayTokenInfo(IDictionary<string, string> tokenInfo)
        {
            LogVerbose($"Token contains {tokenInfo.Count} claims");
            
            if (tokenInfo.TryGetValue("name", out var name))
            {
                Console.Write($"User: {name}");
                LogVerbose($"Found 'name' claim: {name}");
                
                if (tokenInfo.TryGetValue("email", out var email) || 
                    tokenInfo.TryGetValue("upn", out email))
                {
                    Console.Write($" ({email})");
                    LogVerbose($"Found email/upn claim: {email}");
                }
                
                Console.WriteLine();
            }
            
            if (tokenInfo.TryGetValue("exp", out var expString) && 
                long.TryParse(expString, out var expLong))
            {
                var expDate = DateTimeOffset.FromUnixTimeSeconds(expLong).DateTime;
                Console.WriteLine($"Expires: {expDate:yyyy-MM-ddTHH:mm:ssZ}");
                LogVerbose($"Token expires at Unix timestamp: {expLong}");
            }
            
            // Display additional claims that might be useful
            if (_verbose)
            {
                Console.WriteLine("\nAll token claims:");
                foreach (var claim in tokenInfo)
                {
                    Console.WriteLine($"  {claim.Key}: {claim.Value}");
                }
            }
            else
            {
                // Display additional claims that might be useful
                foreach (var claim in tokenInfo)
                {
                    if (claim.Key != "name" && claim.Key != "email" && 
                        claim.Key != "upn" && claim.Key != "exp" &&
                        !claim.Key.StartsWith("aud") && !claim.Key.StartsWith("iss"))
                    {
                        Console.WriteLine($"{claim.Key}: {claim.Value}");
                    }
                }
            }
        }
        
        private static ServiceProvider BuildServiceProvider()
        {
            // Build configuration
            LogVerbose("Building configuration from appsettings.json...");
            var configuration = new ConfigurationBuilder()
                .SetBasePath(GetConfigurationDirectory())
                .AddJsonFile("appsettings.json", optional: false)
                .Build();
            
            // Create service collection
            LogVerbose("Creating service collection...");
            var services = new ServiceCollection();
            
            // Add logging
            LogVerbose("Configuring logging...");
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(_verbose ? LogLevel.Debug : LogLevel.Information);
            });
            
            // Add ADFS authentication service
            LogVerbose("Adding ADFS authentication service...");
            services.AddAdfsAuth(options =>
            {
                // Bind options from configuration
                LogVerbose("Binding options from configuration...");
                configuration.GetSection("AdfsAuth").Bind(options);
                
                // Enable automated features
                LogVerbose("Enabling automated features...");
                options.CacheTokens = true;
                options.AutoRefreshTokens = true;
                options.LoadCredentialsFromEnvironment = true;
                options.Headless = true;
                
                // Set debug options if verbose
                if (_verbose)
                {
                    LogVerbose("Enabling debug options...");
                    options.SlowMo = 200; // Slow down automation for better visibility
                }
            });
            
            // Replace the default AdfsAuthService with our enhanced version
            services.AddTransient<IAdfsAuthService>(provider => 
            {
                var options = provider.GetRequiredService<IOptions<AdfsAuthOptions>>();
                var logger = provider.GetRequiredService<ILogger<EnhancedAdfsAuthService>>();
                return new EnhancedAdfsAuthService(options, logger, _verbose);
            });
            
            // Build service provider
            LogVerbose("Building service provider...");
            return services.BuildServiceProvider();
        }
        
        private static void LogVerbose(string message)
        {
            if (_verbose)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"[DEBUG] {message}");
                Console.ResetColor();
            }
        }
    }
    
    // Custom logger for capturing debug information
    public class DebugLogger
    {
        private readonly bool _verbose;
        
        public DebugLogger(bool verbose)
        {
            _verbose = verbose;
        }
        
        public void Log(string message)
        {
            if (_verbose)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"[DEBUG] {message}");
                Console.ResetColor();
            }
        }
    }
}
