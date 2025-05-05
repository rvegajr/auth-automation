# ADFS Authentication Library Usage Guide

This project demonstrates how to use the ADFS Authentication Library to automate authentication with ADFS servers. The library provides a simple way to obtain tokens for use with ADFS-protected APIs and services.

## Prerequisites

- .NET 8.0 SDK or later
- Node.js 18.x or later
- NPM 9.x or later

## Quick Start

1. Clone this repository
2. Create a `.parameters` file in the root directory with your credentials:
   ```json
   {
     "Username": "your-username",
     "Password": "your-password"
   }
   ```
3. Run the application:
   ```bash
   ./run.sh authenticate
   ```

## Configuration

The application is configured using the `appsettings.json` file. Here's an example configuration:

```json
{
  "AdfsAuth": {
    "Authority": "https://your-adfs-server.com",
    "ClientId": "your-client-id",
    "RedirectUri": "your-redirect-uri",
    "Scope": "openid offline_access",
    "OutputFile": "tokens.json",
    "Headless": true,
    "SlowMo": 50,
    "Timeout": 300000,
    "CredentialsJsonFile": "../.parameters",
    "Screenshots": {
      "Enabled": true,
      "Directory": "screenshots",
      "Prefix": "auth_"
    },
    "TokenCacheFile": "adfs_token_cache.json",
    "AutoInstallDependencies": true
  }
}
```

## Using the Library in Your Application

### 1. Install the NuGet Package

```bash
dotnet add package AdfsAuth
```

### 2. Configure the Service

```csharp
// Add to your Program.cs or Startup.cs
services.AddOptions<AdfsAuthOptions>()
    .Configure<IConfiguration>((options, configuration) => 
    {
        configuration.GetSection("AdfsAuth").Bind(options);
    });

services.AddTransient<IAdfsAuthService, EnhancedAdfsAuthService>();
```

### 3. Use the Service in Your Application

```csharp
// Example of using the service in a controller or service
public class AuthenticationService
{
    private readonly IAdfsAuthService _adfsAuthService;

    public AuthenticationService(IAdfsAuthService adfsAuthService)
    {
        _adfsAuthService = adfsAuthService;
    }

    public async Task<string> GetAccessTokenAsync()
    {
        var result = await _adfsAuthService.AuthenticateAsync();
        
        if (result.Success)
        {
            return result.AccessToken;
        }
        
        throw new Exception($"Authentication failed: {result.Error}");
    }
}
```

### 4. Using the Token with an API

```csharp
public class ApiClient
{
    private readonly HttpClient _httpClient;
    private readonly AuthenticationService _authService;

    public ApiClient(HttpClient httpClient, AuthenticationService authService)
    {
        _httpClient = httpClient;
        _authService = authService;
    }

    public async Task<string> GetProtectedResourceAsync(string url)
    {
        // Get the token
        var token = await _authService.GetAccessTokenAsync();
        
        // Add the token to the request
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        
        // Make the request
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadAsStringAsync();
    }
}
```

## Command Line Interface

The CLI provides several commands for working with ADFS authentication:

### Authenticate

```bash
./run.sh authenticate [--verbose]
```

Authenticates with ADFS and saves the tokens to the configured output file.

### Clear Cache

```bash
./run.sh clear-cache [--verbose]
```

Clears any cached tokens.

### Show Tokens

```bash
./run.sh show-tokens [--verbose]
```

Shows any cached tokens.

### Show Credentials

```bash
./run.sh show-credentials [--verbose]
```

Shows the configured credentials.

### Show Token File

```bash
./run.sh show-token-file --file <path-to-token-file> [--verbose]
```

Shows the contents of a specific token file and parses the JWT tokens.

## Advanced Usage

### Environment Variables

Instead of using a `.parameters` file, you can set environment variables:

```bash
export ADFS_USERNAME="your-username"
export ADFS_PASSWORD="your-password"
```

Then update your `appsettings.json` to use these environment variables:

```json
{
  "AdfsAuth": {
    "UsernameEnvironmentVariable": "ADFS_USERNAME",
    "PasswordEnvironmentVariable": "ADFS_PASSWORD"
  }
}
```

### Screenshots

The library can take screenshots during the authentication process to help with debugging:

```json
{
  "AdfsAuth": {
    "Screenshots": {
      "Enabled": true,
      "Directory": "screenshots",
      "Prefix": "auth_"
    }
  }
}
```

### Token Caching

The library can cache tokens to avoid unnecessary authentication requests:

```json
{
  "AdfsAuth": {
    "TokenCacheFile": "adfs_token_cache.json"
  }
}
```

To use cached tokens:

```csharp
var cachedTokens = _adfsAuthService.GetCachedTokens();
if (cachedTokens != null && !cachedTokens.IsExpired())
{
    return cachedTokens.AccessToken;
}

// Fall back to authentication if no valid cached tokens
var result = await _adfsAuthService.AuthenticateAsync();
```

## Troubleshooting

### Verbose Logging

Add the `--verbose` flag to any command to enable detailed logging:

```bash
./run.sh authenticate --verbose
```

### Common Issues

1. **Missing Credentials**: Ensure your `.parameters` file exists and contains valid credentials.
2. **Timeout Issues**: Increase the timeout value in `appsettings.json`.
3. **Browser Automation Issues**: Try disabling headless mode for debugging.
4. **File Access Issues**: Ensure the application has write permissions to the output directory.

## License

This project is licensed under the MIT License - see the LICENSE file for details.
