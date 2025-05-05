# ADFS Authentication Automation

[![NuGet](https://img.shields.io/nuget/v/AdfsAuth.svg)](https://www.nuget.org/packages/AdfsAuth/)
[![NuGet](https://img.shields.io/nuget/v/AdfsAuthTool.svg)](https://www.nuget.org/packages/AdfsAuthTool/)

**Version: 1.0.1**

A .NET library and global tool for automating ADFS authentication using browser automation.

## Overview

This project provides a solution for automating ADFS authentication in .NET applications. It consists of two main components:

1. **AdfsAuth Library** - A .NET library that handles ADFS authentication using browser automation with Playwright.
2. **AdfsAuthTool** - A .NET global tool that provides a command-line interface for the AdfsAuth library.

The library uses browser automation to handle complex authentication flows, including multi-factor authentication, and can be integrated into any .NET application.

## Features

- **Browser Automation**: Uses Playwright to automate the authentication process
- **Token Caching**: Caches tokens to avoid unnecessary authentication requests
- **Token Refresh**: Automatically refreshes tokens before they expire
- **Headless Mode**: Can run in headless mode for server environments
- **Screenshot Capture**: Can capture screenshots during the authentication process for debugging
- **Verbose Logging**: Provides detailed logging for troubleshooting
- **Global Tool**: Can be installed as a .NET global tool for easy command-line access

## New in Version 1.0.1

- **Token Caching**: Tokens are now cached and reused until they expire, reducing the number of authentication requests.
- **File Access Improvements**: Implemented a more robust file handling mechanism using temporary directories and unique filenames to prevent file locks.
- **Custom Parameters File**: Added a command-line option to specify a custom parameters file for authentication. The tool can now run without an appsettings.json file if a parameters file with all necessary configuration is provided.
- **Automatic Parameters File Creation**: The tool now automatically creates a template parameters file in the current directory when run for the first time.
- **Enhanced Error Handling**: Improved error handling and reporting for better troubleshooting.

## Getting Started

### Prerequisites

- .NET 8.0 SDK or later
- Node.js 18.x or later
- NPM 9.x or later

### Installation

#### Library

Add the AdfsAuth library to your project:

```bash
dotnet add package AdfsAuth
```

#### Global Tool

Install the global tool:

```bash
dotnet tool install --global AdfsAuthTool
```

### Usage

#### Library

```csharp
// Add using statements
using AdfsAuth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

// Set up dependency injection
var services = new ServiceCollection();
services.Configure<AdfsAuthOptions>(options => 
{
    options.Authority = "https://adfs.example.com";
    options.ClientId = "your-client-id";
    options.RedirectUri = "your-redirect-uri";
    options.Scope = "openid offline_access";
    options.CredentialsJsonFile = ".parameters";
});

// Register the service
services.AddTransient<IAdfsAuthService, AdfsAuthService>();

var serviceProvider = services.BuildServiceProvider();

// Use the service
var adfsAuthService = serviceProvider.GetRequiredService<IAdfsAuthService>();
var result = await adfsAuthService.AuthenticateAsync();

if (result.Success)
{
    // Use the tokens
    var accessToken = result.AccessToken;
    // ...
}
```

#### Global Tool

```bash
# Authenticate with ADFS
adfs-auth authenticate

# Show verbose output for debugging
adfs-auth authenticate --verbose

# Clear the token cache
adfs-auth clear-cache

# Show cached tokens
adfs-auth show-tokens

# Show credentials configuration
adfs-auth show-credentials

# Show tokens from a specific file
adfs-auth show-token-file --file /path/to/tokens.json
```

## Configuration

The library and tool can be configured in multiple ways:

#### 1. Using appsettings.json

```json
{
  "AdfsAuth": {
    "Authority": "https://adfs.example.com",
    "ClientId": "your-client-id",
    "RedirectUri": "your-redirect-uri",
    "Scope": "openid offline_access",
    "OutputFile": "tokens.json",
    "Headless": true,
    "SlowMo": 50,
    "Timeout": 300000,
    "CredentialsJsonFile": ".parameters",
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

#### 2. Using a Parameters File

You can specify all configuration options in the parameters file (not just credentials). This approach keeps sensitive information separate from your application configuration:

```json
{
  "Username": "user@example.com",
  "Password": "your-password-here",
  "Authority": "https://adfs.example.com",
  "ClientId": "your-client-id",
  "RedirectUri": "https://app.example.com/redirect",
  "Scope": "openid offline_access",
  "Headless": true,
  "SlowMo": 50,
  "Timeout": 300000,
  "OutputFile": "tokens.json",
  "TokenCacheFile": "adfs_token_cache.json",
  "Screenshots": {
    "Enabled": true,
    "Directory": "screenshots",
    "Prefix": "auth_"
  }
}
```

To use this approach, specify the path to your parameters file in the `CredentialsJsonFile` setting in your code or appsettings.json:

```csharp
services.Configure<AdfsAuthOptions>(options => 
{
    options.CredentialsJsonFile = ".parameters";
    // You don't need to specify other options here if they're in the parameters file
});
```

#### 3. Using Environment Variables

For credentials, you can use environment variables:
- `ADFS_USERNAME` - The username for ADFS authentication
- `ADFS_PASSWORD` - The password for ADFS authentication

To use environment variables, set the following options:

```csharp
services.Configure<AdfsAuthOptions>(options => 
{
    options.LoadCredentialsFromEnvironment = true;
    options.UsernameEnvironmentVariable = "ADFS_USERNAME"; // Optional, defaults to ADFS_USERNAME
    options.PasswordEnvironmentVariable = "ADFS_PASSWORD"; // Optional, defaults to ADFS_PASSWORD
});
```

#### Configuration Priority

When multiple configuration sources are specified, the library uses the following priority order:

1. Explicitly set options in code
2. Parameters file (if specified and found)
3. Environment variables (if enabled)
4. Default values

## Performance Optimizations

The ADFS Authentication tool includes several performance optimizations:

### Token Caching

The tool automatically caches tokens to improve performance and reduce the need for browser automation:

- Tokens are cached in a local file (`adfs_token_cache.json` by default)
- When requesting a new token, the tool first checks if a valid cached token exists
- If a valid token is found (not expired), it's returned immediately without launching the browser
- This significantly improves performance for repeated API calls

To clear the token cache:

```bash
adfs-auth clear-cache
```

### File Access Improvements

To prevent file access issues when running the tool:

- The tool uses a unique temporary directory for each run to avoid file locks
- Node.js scripts are copied with unique filenames to prevent conflicts
- A cleanup script (`cleanup_node_files.sh`) is provided to clear any locked files before running

If you encounter file access issues, run the cleanup script:

```bash
./cleanup_node_files.sh
```

## Building from Source

1. Clone the repository
2. Build the solution:
   ```bash
   dotnet build
   ```
3. Run the tests:
   ```bash
   dotnet test
   ```

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.