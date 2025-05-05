# ADFS Authentication CLI Global Tool

**Version: 1.0.1**

This is a .NET global tool for automating ADFS authentication from the command line. It provides a simple way to obtain tokens for use with ADFS-protected APIs and services.

## Installation

### From NuGet (once published)

```bash
dotnet tool install --global AdfsAuthTool
```

### From Local Source

1. Clone the repository
2. Build and package the tool:
   ```bash
   cd Src/AdfsAuthTool
   dotnet pack
   ```
3. Install the tool locally:
   ```bash
   dotnet tool install --global --add-source ./nupkg AdfsAuthTool
   ```

## Usage

After installation, you can use the tool from anywhere by running:

```bash
adfs-auth [command] [options]
```

### Available Commands

- `authenticate`: Authenticate with ADFS and get tokens
- `clear-cache`: Clear cached tokens
- `show-tokens`: Show cached tokens if available
- `show-credentials`: Show the credentials that will be used for authentication
- `show-token-file`: Show tokens from a specific file

### Global Options

- `-v, --verbose`: Enable verbose output for detailed debugging information
- `--version`: Show version information
- `-h, --help`: Show help and usage information

### Examples

```bash
# Authenticate and get tokens
adfs-auth authenticate --verbose

# Authenticate using a custom parameters file
adfs-auth authenticate --parameters-file /path/to/custom.parameters

# Show the current credentials configuration
adfs-auth show-credentials

# Show credentials using a custom parameters file
adfs-auth show-credentials --parameters-file /path/to/custom.parameters

# Show the contents of a token file
adfs-auth show-token --file tokens.json

# Clear the token cache
adfs-auth clear-cache
```

## Configuration

The tool can be configured using:

1. The default `appsettings.json` file in the tool directory
2. A `.parameters` file in the current directory or parent directory
3. A custom parameters file specified with the `--parameters-file` option

**Note:** You can run the tool without an `appsettings.json` file by providing a parameters file that contains all the necessary configuration values.

**Automatic Parameters File Creation:** When you run the tool for the first time in a directory, it will automatically create a `.parameters` template file if one doesn't already exist. You can edit this file with your ADFS credentials and configuration.

### Parameters File Format

The parameters file can be in one of two formats:

1. **Full Configuration Format** - Contains both ADFS configuration and credentials:

```json
{
  "Authority": "https://adfs.example.com/adfs/",
  "ClientId": "your-client-id",
  "RedirectUri": "https://app.example.com/redirect",
  "Scope": "openid profile email offline_access",
  "Username": "your-username",
  "Password": "your-password"
}
```

2. **Credentials-Only Format** - Contains just the username and password:

```json
{
  "Username": "your-username",
  "Password": "your-password"
}
```

When using the `--parameters-file` option, the tool will:
1. Load the base configuration from `appsettings.json` (if available)
2. Override it with values from the custom parameters file
3. Use the custom parameters file for credentials

## Performance Optimizations

The tool includes several performance optimizations:

- **Token Caching**: Tokens are cached to improve performance and reduce the need for browser automation
- **File Access Improvements**: Unique temporary directories and filenames prevent file locks

## Troubleshooting

If you encounter file access issues, run the included cleanup script:

```bash
./cleanup_node_files.sh
```

## Uninstallation

```bash
dotnet tool uninstall --global AdfsAuthTool
```

## Prerequisites

- .NET 8.0 SDK or later
- Node.js 18.x or later
- NPM 9.x or later

## License

This project is licensed under the MIT License - see the LICENSE file for details.
