using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AdfsAuth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AdfsAutomationUsage
{
    /// <summary>
    /// Enhanced ADFS authentication service that provides real-time output from the Node.js script.
    /// </summary>
    public class EnhancedAdfsAuthService : IAdfsAuthService
    {
        private readonly IOptions<AdfsAuthOptions> _options;
        private readonly ILogger<EnhancedAdfsAuthService> _logger;
        private readonly bool _verbose;
        private readonly string _screenshotsDir;
        private readonly bool _screenshotsEnabled;
        private readonly string _screenshotsPrefix;

        public EnhancedAdfsAuthService(IOptions<AdfsAuthOptions> options, ILogger<EnhancedAdfsAuthService> logger, bool verbose = false)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _verbose = verbose;
            
            // Configure screenshots
            _screenshotsEnabled = false; // Default to false
            _screenshotsDir = "screenshots";
            _screenshotsPrefix = "auth_";
            
            // Check if screenshots directory is specified in appsettings.json
            var screenshotsSection = options.Value.GetType().GetProperty("Screenshots");
            if (screenshotsSection != null)
            {
                var screenshots = screenshotsSection.GetValue(options.Value);
                if (screenshots != null)
                {
                    var enabledProp = screenshots.GetType().GetProperty("Enabled");
                    if (enabledProp != null)
                    {
                        var enabledValue = enabledProp.GetValue(screenshots);
                        if (enabledValue != null)
                        {
                            _screenshotsEnabled = (bool)enabledValue;
                        }
                    }
                    
                    var dirProp = screenshots.GetType().GetProperty("Directory");
                    if (dirProp != null)
                    {
                        var dir = dirProp.GetValue(screenshots) as string;
                        if (!string.IsNullOrEmpty(dir))
                        {
                            _screenshotsDir = dir;
                        }
                    }
                    
                    var prefixProp = screenshots.GetType().GetProperty("Prefix");
                    if (prefixProp != null)
                    {
                        var prefix = prefixProp.GetValue(screenshots) as string;
                        if (!string.IsNullOrEmpty(prefix))
                        {
                            _screenshotsPrefix = prefix;
                        }
                    }
                }
            }
            
            if (_screenshotsEnabled)
            {
                // Create screenshots directory if it doesn't exist
                if (!Directory.Exists(_screenshotsDir))
                {
                    Directory.CreateDirectory(_screenshotsDir);
                    Console.WriteLine($"Created screenshots directory: {_screenshotsDir}");
                }
            }
        }

        public async Task<AdfsAuthResult> AuthenticateAsync()
        {
            // Use our custom Node.js script runner
            return await RunNodeScriptWithRealtimeOutputAsync();
        }

        public Task<AdfsAuthResult> AuthenticateAsync(AdfsAuthOptions options)
        {
            // For simplicity, we're not implementing this overload
            throw new NotImplementedException("This method is not implemented in the enhanced service.");
        }

        public void ClearCachedTokens()
        {
            // Simple implementation to clear the token cache file
            var cacheFile = _options.Value.TokenCacheFile;
            if (!string.IsNullOrEmpty(cacheFile) && File.Exists(cacheFile))
            {
                File.Delete(cacheFile);
                Console.WriteLine($"Deleted token cache file: {cacheFile}");
            }
        }

        public AdfsAuthResult? GetCachedTokens()
        {
            // Simple implementation to read from the token cache file
            var cacheFile = _options.Value.TokenCacheFile;
            if (string.IsNullOrEmpty(cacheFile) || !File.Exists(cacheFile))
            {
                return null;
            }

            try
            {
                var json = File.ReadAllText(cacheFile);
                var result = JsonSerializer.Deserialize<AdfsAuthResult>(json);
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading token cache: {ex.Message}");
                return null;
            }
        }

        public IDictionary<string, string> ParseToken(string token)
        {
            // Simple implementation of token parsing
            var parts = token.Split('.');
            if (parts.Length != 3)
            {
                return new Dictionary<string, string>();
            }

            try
            {
                // Decode the payload (second part)
                var payload = parts[1];
                // Add padding if needed
                while (payload.Length % 4 != 0)
                {
                    payload += "=";
                }
                
                // Replace URL-safe characters
                payload = payload.Replace('-', '+').Replace('_', '/');
                
                // Decode base64
                var jsonBytes = Convert.FromBase64String(payload);
                var json = System.Text.Encoding.UTF8.GetString(jsonBytes);
                
                // Parse JSON
                using var doc = JsonDocument.Parse(json);
                var claims = new Dictionary<string, string>();
                
                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    claims[property.Name] = property.Value.ToString();
                }
                
                return claims;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing token: {ex.Message}");
                return new Dictionary<string, string>();
            }
        }

        private async Task<AdfsAuthResult> RunNodeScriptWithRealtimeOutputAsync()
        {
            try
            {
                // Get the node script path from the inner service
                var nodeDir = Path.Combine(AppContext.BaseDirectory, "node");
                if (!Directory.Exists(nodeDir))
                {
                    Directory.CreateDirectory(nodeDir);
                }

                // Copy the node scripts to the output directory
                CopyNodeScripts(nodeDir);

                // Install dependencies if needed
                if (_options.Value.AutoInstallDependencies)
                {
                    await InstallNodeDependenciesAsync(nodeDir);
                }

                // Create the config file
                var configPath = Path.Combine(nodeDir, "config.json");
                var config = CreateNodeConfig();
                
                // Check if we need to prompt for credentials
                if (!config.ContainsKey("username") || !config.ContainsKey("password"))
                {
                    Console.WriteLine("Credentials not found in environment variables or configuration file.");
                    
                    if (!config.ContainsKey("username"))
                    {
                        Console.Write("Enter username: ");
                        var username = Console.ReadLine();
                        if (!string.IsNullOrEmpty(username))
                        {
                            config["username"] = username;
                        }
                    }
                    
                    if (!config.ContainsKey("password"))
                    {
                        Console.Write("Enter password: ");
                        var password = ReadPassword();
                        if (!string.IsNullOrEmpty(password))
                        {
                            config["password"] = password;
                        }
                    }
                }
                
                // Create a sanitized copy of the config for writing to file
                // This removes sensitive information (password) before writing to disk
                var configForFile = new Dictionary<string, object>(config);
                if (configForFile.ContainsKey("password"))
                {
                    configForFile["password"] = "***REDACTED***";
                }
                
                // Write sanitized config to disk for debugging
                await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(configForFile, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
                
                // Create a temporary config file with the actual credentials (will be deleted after use)
                var tempConfigPath = Path.Combine(nodeDir, "config.temp.json");
                await File.WriteAllTextAsync(tempConfigPath, JsonSerializer.Serialize(config, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

                // Run the node script with the temporary config that contains actual credentials
                var scriptPath = Path.Combine(nodeDir, "adfs-auth-cli.js");
                Console.WriteLine($"Running Node.js script: {scriptPath}");

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "node",
                    Arguments = $"\"{scriptPath}\" \"{tempConfigPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = nodeDir
                };

                using var process = Process.Start(processStartInfo);
                if (process == null)
                {
                    throw new Exception("Failed to start Node.js process");
                }

                // Set up event handlers for real-time output
                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        if (e.Data.Contains("Error:") || e.Data.Contains("error:"))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"[Node] {e.Data}");
                            Console.ResetColor();
                        }
                        else if (e.Data.Contains("success") || e.Data.Contains("Success"))
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"[Node] {e.Data}");
                            Console.ResetColor();
                        }
                        else if (_verbose || IsImportantMessage(e.Data))
                        {
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.WriteLine($"[Node] {e.Data}");
                            Console.ResetColor();
                        }
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"[Node Error] {e.Data}");
                        Console.ResetColor();
                    }
                };

                // Start reading output
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Wait for the process to exit
                await process.WaitForExitAsync();
                
                // Clean up the temporary config file with real credentials
                try
                {
                    if (File.Exists(tempConfigPath))
                    {
                        File.Delete(tempConfigPath);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to delete temporary config file: {ex.Message}");
                }
                
                // Get the output
                string output = File.Exists(_options.Value.OutputFile) 
                    ? await File.ReadAllTextAsync(_options.Value.OutputFile) 
                    : "{}";

                if (process.ExitCode != 0)
                {
                    Console.WriteLine($"Node.js script failed with exit code {process.ExitCode}");
                    return new AdfsAuthResult
                    {
                        Success = false,
                        Error = $"Node.js script failed with exit code {process.ExitCode}",
                        ErrorDescription = "See above for detailed error information"
                    };
                }

                try
                {
                    // Try to parse the output file first
                    if (File.Exists(_options.Value.OutputFile))
                    {
                        var result = JsonSerializer.Deserialize<AdfsAuthResult>(output);
                        if (result != null)
                        {
                            // Check if we got tokens even in an error case
                            if (result.Success)
                            {
                                Console.WriteLine("✅ Authentication successful!");
                                return result;
                            }
                            else if (result.Tokens != null && result.Tokens.AccessToken != null)
                            {
                                // We have tokens but possibly an authorization issue
                                if (result.Error == "Unauthorized access" || 
                                    (result.FinalUrl != null && result.FinalUrl.Contains("/login/unauthorized")))
                                {
                                    Console.WriteLine("⚠️ Authentication successful but user is not authorized for this application");
                                    Console.WriteLine("Tokens were captured and can be used for API calls");
                                    
                                    // Override success to true since we have tokens
                                    result.Success = true;
                                    return result;
                                }
                                return result;
                            }
                            else
                            {
                                // Regular error case
                                Console.WriteLine($"❌ Authentication failed!");
                                Console.WriteLine($"Error: {result.Error}");
                                Console.WriteLine($"Description: {result.ErrorDescription}");
                                return result;
                            }
                        }
                    }
                    
                    // Extract the JSON result from the output
                    // Look for the last occurrence of a JSON object in the output
                    var jsonStart = output.LastIndexOf('{');
                    var jsonEnd = output.LastIndexOf('}');
                    
                    if (jsonStart >= 0 && jsonEnd > jsonStart)
                    {
                        var jsonResult = output.Substring(jsonStart, jsonEnd - jsonStart + 1);
                        if (_verbose)
                        {
                            Console.WriteLine($"Parsed JSON result: {jsonResult}");
                        }
                        return JsonSerializer.Deserialize<AdfsAuthResult>(jsonResult) ?? 
                            new AdfsAuthResult { Success = false, Error = "Failed to parse JSON result" };
                    }
                    else
                    {
                        Console.WriteLine($"Failed to find JSON result in output");
                        return new AdfsAuthResult
                        {
                            Success = false,
                            Error = "Failed to find JSON result in output",
                            ErrorDescription = "See above for detailed error information"
                        };
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to parse Node.js script output: {ex.Message}");
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
                Console.WriteLine($"Error running Node.js script: {ex.Message}");
                if (_verbose)
                {
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                }
                return new AdfsAuthResult
                {
                    Success = false,
                    Error = "Error running Node.js script",
                    ErrorDescription = ex.Message
                };
            }
        }

        private bool IsImportantMessage(string message)
        {
            // List of keywords that indicate important messages to always show
            var importantKeywords = new[]
            {
                "navigating", "redirect", "login", "username", "password", "button",
                "waiting", "timeout", "selector", "screenshot", "error", "token",
                "entering", "clicking", "found", "detected"
            };

            foreach (var keyword in importantKeywords)
            {
                if (message.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void CopyNodeScripts(string nodeDir)
        {
            Console.WriteLine("Copying Node.js scripts...");

            try
            {
                // Try to run the cleanup script first if it exists
                var cleanupScript = Path.Combine(AppContext.BaseDirectory, "cleanup_node_files.sh");
                if (File.Exists(cleanupScript))
                {
                    try
                    {
                        if (_verbose) Console.WriteLine("Running cleanup script to release locked files...");
                        var processStartInfo = new ProcessStartInfo
                        {
                            FileName = "bash",
                            Arguments = $"\"{cleanupScript}\"",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        using var process = Process.Start(processStartInfo);
                        if (process != null)
                        {
                            process.WaitForExit();
                            if (_verbose) Console.WriteLine($"Cleanup script exited with code: {process.ExitCode}");
                            // Add a small delay after cleanup to ensure processes are fully terminated
                            Task.Delay(1000).Wait();
                        }
                    }
                    catch (Exception ex)
                    {
                        if (_verbose) Console.WriteLine($"Error running cleanup script: {ex.Message}");
                    }
                }
                else
                {
                    if (_verbose) Console.WriteLine("No cleanup script found. Skipping cleanup step.");
                }

                // Get the source directory from the assembly location
                var assemblyLocation = typeof(IAdfsAuthService).Assembly.Location;
                var assemblyDir = Path.GetDirectoryName(assemblyLocation) ?? throw new InvalidOperationException("Could not determine assembly directory");
                var sourceNodeDir = Path.Combine(assemblyDir, "node");

                // Ensure the destination directory exists
                Directory.CreateDirectory(nodeDir);
                
                // Copy the node scripts to the output directory - use a timestamp-based approach for all files
                if (Directory.Exists(sourceNodeDir))
                {
                    // Generate a timestamp suffix for this run to ensure uniqueness
                    var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                    var filesCopied = new Dictionary<string, string>(); // Track original name to new name
                    
                    foreach (var file in Directory.GetFiles(sourceNodeDir))
                    {
                        try
                        {
                            var fileName = Path.GetFileName(file);
                            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(file);
                            var fileExt = Path.GetExtension(file);
                            
                            // Always use a unique filename with timestamp to avoid locking issues
                            var uniqueFileName = $"{fileNameWithoutExt}_{timestamp}{fileExt}";
                            var destFile = Path.Combine(nodeDir, uniqueFileName);
                            
                            // Remember the mapping from original to new filename
                            filesCopied[fileName] = uniqueFileName;
                            
                            // Copy the file - with a generous retry mechanism
                            for (int attempt = 1; attempt <= 5; attempt++)
                            {
                                try
                                {
                                    // Use File.ReadAllBytes/WriteAllBytes which can sometimes avoid locking issues
                                    byte[] fileContents = File.ReadAllBytes(file);
                                    File.WriteAllBytes(destFile, fileContents);
                                    
                                    if (_verbose) Console.WriteLine($"Copied {fileName} to {destFile} (attempt {attempt})");
                                    break;
                                }
                                catch (IOException ex) when (attempt < 5)
                                {
                                    Console.WriteLine($"Failed to copy {fileName}: {ex.Message}. Attempt {attempt} of 5.");
                                    // Try a different filename for the next attempt
                                    destFile = Path.Combine(nodeDir, $"{fileNameWithoutExt}_{timestamp}_{attempt}{fileExt}");
                                    Task.Delay(1000).Wait(); // Wait longer between retries
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error copying file: {ex.Message}");
                            if (_verbose)
                            {
                                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                            }
                        }
                    }
                    
                    // Create a special file mapping.json to help the Node script find its dependencies
                    try 
                    {
                        var mappingFile = Path.Combine(nodeDir, "file_mapping.json");
                        File.WriteAllText(mappingFile, System.Text.Json.JsonSerializer.Serialize(filesCopied, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                        if (_verbose) Console.WriteLine($"Created file mapping at {mappingFile}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error creating file mapping: {ex.Message}");
                    }
                }
                else
                {
                    throw new DirectoryNotFoundException($"Node.js scripts directory not found: {sourceNodeDir}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error copying Node.js scripts: {ex.Message}");
                if (_verbose)
                {
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                }
            }
        }

        private async Task InstallNodeDependenciesAsync(string nodeDir)
        {
            Console.WriteLine("Installing Node.js dependencies...");
            
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "npm",
                Arguments = "install",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = nodeDir
            };

            using var process = Process.Start(processStartInfo);
            if (process == null)
            {
                throw new Exception("Failed to start npm process");
            }

            // Set up event handlers for real-time output if verbose
            if (_verbose)
            {
                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine($"[npm] {e.Data}");
                        Console.ResetColor();
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"[npm error] {e.Data}");
                        Console.ResetColor();
                    }
                };

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }

            await process.WaitForExitAsync();
            
            if (process.ExitCode != 0)
            {
                throw new Exception($"Failed to install Node.js dependencies. Exit code: {process.ExitCode}");
            }
            
            Console.WriteLine("Node.js dependencies installed successfully");
        }

        private Dictionary<string, object> CreateNodeConfig()
        {
            // Create a dictionary for the config to avoid property name conflicts
            var configDict = new Dictionary<string, object>
            {
                { "authority", _options.Value.Authority ?? string.Empty },
                { "clientId", _options.Value.ClientId ?? string.Empty },
                { "redirectUri", _options.Value.RedirectUri ?? string.Empty },
                { "scope", _options.Value.Scope ?? string.Empty },
                { "headless", _options.Value.Headless },
                { "slowMo", _options.Value.SlowMo },
                { "timeout", _options.Value.Timeout },
                { "outputFile", _options.Value.OutputFile ?? "tokens.json" },
                { "debug", _verbose }
            };
            
            // First check environment variables for credentials
            var envUsername = Environment.GetEnvironmentVariable("ADFS_AUTH_USERNAME");
            var envPassword = Environment.GetEnvironmentVariable("ADFS_AUTH_PASSWORD");
            
            if (!string.IsNullOrEmpty(envUsername))
            {
                configDict["username"] = envUsername;
                Console.WriteLine("Username loaded from environment variable");
            }
            
            if (!string.IsNullOrEmpty(envPassword))
            {
                configDict["password"] = envPassword;
                Console.WriteLine("Password loaded from environment variable");
            }
            
            // Then try to load configuration from the JSON file
            var credentialsFile = _options.Value.CredentialsJsonFile;
            if (!string.IsNullOrEmpty(credentialsFile) && File.Exists(credentialsFile))
            {
                try
                {
                    Console.WriteLine($"Loading configuration from: {credentialsFile}");
                    var jsonContent = File.ReadAllText(credentialsFile);
                    var parametersDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonContent);
                    
                    if (parametersDict != null)
                    {
                        // Only load username/password from file if not already set from environment variables
                        if (!configDict.ContainsKey("username") && 
                            (parametersDict.TryGetValue("Username", out var usernameElement) || 
                             parametersDict.TryGetValue("username", out usernameElement)))
                        {
                            var username = usernameElement.GetString();
                            if (!string.IsNullOrEmpty(username))
                            {
                                configDict["username"] = username;
                                Console.WriteLine("Username loaded from configuration file");
                            }
                        }
                        
                        if (!configDict.ContainsKey("password") && 
                            (parametersDict.TryGetValue("Password", out var passwordElement) || 
                             parametersDict.TryGetValue("password", out passwordElement)))
                        {
                            var password = passwordElement.GetString();
                            if (!string.IsNullOrEmpty(password))
                            {
                                configDict["password"] = password;
                                Console.WriteLine("Password loaded from configuration file");
                            }
                        }
                        
                        // Process other configuration options
                        ProcessParameterIfExists(parametersDict, "Authority", "authority", configDict);
                        ProcessParameterIfExists(parametersDict, "ClientId", "clientId", configDict);
                        ProcessParameterIfExists(parametersDict, "RedirectUri", "redirectUri", configDict);
                        ProcessParameterIfExists(parametersDict, "Scope", "scope", configDict);
                        ProcessParameterIfExists(parametersDict, "OutputFile", "outputFile", configDict);
                        ProcessParameterIfExists(parametersDict, "TokenCacheFile", "tokenCacheFile", configDict);
                        
                        // Process boolean options
                        ProcessBooleanParameterIfExists(parametersDict, "Headless", "headless", configDict);
                        ProcessBooleanParameterIfExists(parametersDict, "AutoInstallDependencies", "autoInstallDependencies", configDict);
                        
                        // Process numeric options
                        ProcessNumericParameterIfExists(parametersDict, "SlowMo", "slowMo", configDict);
                        ProcessNumericParameterIfExists(parametersDict, "Timeout", "timeout", configDict);
                        
                        // Process Screenshots object if it exists
                        if (parametersDict.TryGetValue("Screenshots", out var screenshotsElement) && 
                            screenshotsElement.ValueKind == JsonValueKind.Object)
                        {
                            var screenshotsDict = new Dictionary<string, object>();
                            
                            try
                            {
                                var screenshotsObj = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                                    screenshotsElement.GetRawText());
                                
                                if (screenshotsObj != null)
                                {
                                    if (screenshotsObj.TryGetValue("Enabled", out var enabledElement) || 
                                        screenshotsObj.TryGetValue("enabled", out enabledElement))
                                    {
                                        if (enabledElement.ValueKind == JsonValueKind.True || 
                                            enabledElement.ValueKind == JsonValueKind.False)
                                        {
                                            screenshotsDict["enabled"] = enabledElement.GetBoolean();
                                        }
                                    }
                                    
                                    if (screenshotsObj.TryGetValue("Directory", out var dirElement) || 
                                        screenshotsObj.TryGetValue("directory", out dirElement))
                                    {
                                        if (dirElement.ValueKind == JsonValueKind.String)
                                        {
                                            screenshotsDict["directory"] = dirElement.GetString();
                                        }
                                    }
                                    
                                    if (screenshotsObj.TryGetValue("Prefix", out var prefixElement) || 
                                        screenshotsObj.TryGetValue("prefix", out prefixElement))
                                    {
                                        if (prefixElement.ValueKind == JsonValueKind.String)
                                        {
                                            screenshotsDict["prefix"] = prefixElement.GetString();
                                        }
                                    }
                                    
                                    if (screenshotsDict.Count > 0)
                                    {
                                        configDict["screenshots"] = screenshotsDict;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error processing screenshots configuration: {ex.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading configuration from file: {ex.Message}");
                }
            }
            else if (!string.IsNullOrEmpty(_options.Value.Username) && !string.IsNullOrEmpty(_options.Value.Password))
            {
                // Fall back to options if credentials file doesn't exist
                configDict["username"] = _options.Value.Username;
                configDict["password"] = _options.Value.Password;
            }
            
            // Fall back to environment variables if credentials are still missing
            if (_options.Value.LoadCredentialsFromEnvironment && 
                (!configDict.ContainsKey("username") || !configDict.ContainsKey("password")))
            {
                var usernameEnvVar = _options.Value.UsernameEnvironmentVariable ?? "ADFS_USERNAME";
                var passwordEnvVar = _options.Value.PasswordEnvironmentVariable ?? "ADFS_PASSWORD";
                
                var username = Environment.GetEnvironmentVariable(usernameEnvVar);
                var password = Environment.GetEnvironmentVariable(passwordEnvVar);
                
                if (!string.IsNullOrEmpty(username) && !configDict.ContainsKey("username"))
                {
                    configDict["username"] = username;
                    Console.WriteLine($"Username loaded from environment variable: {usernameEnvVar}");
                }
                
                if (!string.IsNullOrEmpty(password) && !configDict.ContainsKey("password"))
                {
                    configDict["password"] = password;
                    Console.WriteLine($"Password loaded from environment variable: {passwordEnvVar}");
                }
            }
            
            return configDict;
        }
        
        private void ProcessParameterIfExists(Dictionary<string, JsonElement> parameters, string paramName, 
            string configName, Dictionary<string, object> configDict)
        {
            if (parameters.TryGetValue(paramName, out var element) || 
                parameters.TryGetValue(paramName.ToLower(), out element))
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    var value = element.GetString();
                    if (!string.IsNullOrEmpty(value))
                    {
                        configDict[configName] = value;
                        if (_verbose)
                        {
                            Console.WriteLine($"Loaded {paramName} from parameters file");
                        }
                    }
                }
            }
        }
        
        private void ProcessBooleanParameterIfExists(Dictionary<string, JsonElement> parameters, string paramName, 
            string configName, Dictionary<string, object> configDict)
        {
            if (parameters.TryGetValue(paramName, out var element) || 
                parameters.TryGetValue(paramName.ToLower(), out element))
            {
                if (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False)
                {
                    configDict[configName] = element.GetBoolean();
                    if (_verbose)
                    {
                        Console.WriteLine($"Loaded {paramName} from parameters file");
                    }
                }
            }
        }
        
        private void ProcessNumericParameterIfExists(Dictionary<string, JsonElement> parameters, string paramName, 
            string configName, Dictionary<string, object> configDict)
        {
            if (parameters.TryGetValue(paramName, out var element) || 
                parameters.TryGetValue(paramName.ToLower(), out element))
            {
                if (element.ValueKind == JsonValueKind.Number)
                {
                    configDict[configName] = element.GetInt32();
                    if (_verbose)
                    {
                        Console.WriteLine($"Loaded {paramName} from parameters file");
                    }
                }
            }
        }
        
        /// <summary>
        /// Securely reads a password from the console without displaying the characters.
        /// </summary>
        /// <returns>The password entered by the user</returns>
        private string ReadPassword()
        {
            var password = new StringBuilder();
            ConsoleKeyInfo key;
            
            do {
                key = Console.ReadKey(true); // true means do not display the character
                
                // Only add the key if it's not a control character (like Enter)
                if (key.Key != ConsoleKey.Enter)
                {
                    if (key.Key == ConsoleKey.Backspace && password.Length > 0)
                    {
                        // Handle backspace - remove the last character
                        password.Remove(password.Length - 1, 1);
                        Console.Write("\b \b"); // Erase the character from the console
                    }
                    else if (!char.IsControl(key.KeyChar))
                    {
                        // Add the character to the password and display a mask character
                        password.Append(key.KeyChar);
                        Console.Write("*");
                    }
                }
            } while (key.Key != ConsoleKey.Enter);
            
            Console.WriteLine(); // Move to the next line after Enter is pressed
            return password.ToString();
        }
    }
}
