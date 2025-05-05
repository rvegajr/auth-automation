#!/bin/bash
# Script to clean up any locked Node.js files before running the authentication

# Set timeout for all curl commands
CURL_TIMEOUT="--max-time 5"

# Get the base directory
BASE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
echo "Base directory: $BASE_DIR"

# Define the node directory
NODE_DIR="$BASE_DIR/bin/Debug/net8.0/node"
echo "Node directory: $NODE_DIR"

# Check if the node directory exists
if [ ! -d "$NODE_DIR" ]; then
    echo "Node directory does not exist. Creating it..."
    mkdir -p "$NODE_DIR"
fi

# Clean up any existing node processes
echo "Checking for running node processes..."
NODE_PIDS=$(pgrep -f "node.*adfs-auth")
if [ -n "$NODE_PIDS" ]; then
    echo "Found running node processes. Killing them..."
    echo $NODE_PIDS | xargs kill -9
    echo "Node processes killed."
else
    echo "No running node processes found."
fi

# Remove any existing node files
echo "Removing existing node files..."
rm -f "$NODE_DIR/adfs-auth-cli.js"
rm -f "$NODE_DIR/adfs-auth.js"
rm -f "$NODE_DIR/config.json"
rm -f "$NODE_DIR/package.json"
rm -f "$NODE_DIR/package-lock.json"
echo "Node files removed."

# Clean up any token cache files
echo "Checking for token cache files..."
if [ -f "$BASE_DIR/adfs_token_cache.json" ]; then
    echo "Removing token cache file..."
    rm -f "$BASE_DIR/adfs_token_cache.json"
    echo "Token cache file removed."
else
    echo "No token cache file found."
fi

echo "Cleanup completed successfully."
