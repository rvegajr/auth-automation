using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
                await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

                // Run the node script
                var scriptPath = Path.Combine(nodeDir, "adfs-auth-cli.js");
                Console.WriteLine($"Running Node.js script: {scriptPath}");

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "node",
                    Arguments = $"\"{scriptPath}\" \"{configPath}\"",
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
                        if (result != null && result.Success)
                        {
                            return result;
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

                // Copy the node scripts to the output directory
                if (Directory.Exists(sourceNodeDir))
                {
                    foreach (var file in Directory.GetFiles(sourceNodeDir))
                    {
                        try
                        {
                            var fileName = Path.GetFileName(file);
                            var destFile = Path.Combine(nodeDir, fileName);

                            // Check if file exists and try to release it
                            if (File.Exists(destFile))
                            {
                                try
                                {
                                    // Try to open and close the file to check if it's locked
                                    using (var fs = new FileStream(destFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                                    {
                                        // File is not locked, we can close it
                                    }
                                }
                                catch (IOException)
                                {
                                    // File is locked, let's try to use a different filename
                                    if (_verbose) Console.WriteLine($"File {fileName} is in use. Using a unique filename.");
                                    destFile = Path.Combine(nodeDir, $"{Path.GetFileNameWithoutExtension(fileName)}_{Guid.NewGuid().ToString("N").Substring(0, 8)}{Path.GetExtension(fileName)}");
                                }
                            }

                            // Copy with retry logic
                            int retries = 3;
                            while (retries > 0)
                            {
                                try
                                {
                                    File.Copy(file, destFile, true);
                                    if (_verbose) Console.WriteLine($"Copied {fileName} to {destFile}");
                                    break;
                                }
                                catch (IOException ex) when (retries > 1)
                                {
                                    Console.WriteLine($"Failed to copy {fileName}: {ex.Message}. Retrying...");
                                    retries--;
                                    Task.Delay(500).Wait(); // Wait a bit before retrying
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
            
            // Try to load configuration from the JSON file first
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
                        // Process credentials
                        if (parametersDict.TryGetValue("Username", out var usernameElement) || 
                            parametersDict.TryGetValue("username", out usernameElement))
                        {
                            var username = usernameElement.GetString();
                            if (!string.IsNullOrEmpty(username))
                            {
                                configDict["username"] = username;
                                Console.WriteLine($"Username loaded: {username}");
                            }
                        }
                        
                        if (parametersDict.TryGetValue("Password", out var passwordElement) || 
                            parametersDict.TryGetValue("password", out passwordElement))
                        {
                            var password = passwordElement.GetString();
                            if (!string.IsNullOrEmpty(password))
                            {
                                configDict["password"] = password;
                                Console.WriteLine("Password loaded");
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
    }
}
