#!/bin/bash

# Script to run the ADFS Authentication CLI
echo "=== ADFS Authentication CLI Runner ==="

# Create a symlink to the .parameters file if it exists
PARAMS_FILE="/Users/rickyvega/Dev/AuthTester/.parameters"
if [ -f "$PARAMS_FILE" ]; then
  echo "Found .parameters file at $PARAMS_FILE"
  # Create a symlink to the .parameters file in the current directory
  ln -sf "$PARAMS_FILE" .parameters
else
  echo "Warning: .parameters file not found at $PARAMS_FILE"
fi

# Create screenshots directory if it doesn't exist
SCREENSHOTS_DIR="screenshots"
if [ ! -d "$SCREENSHOTS_DIR" ]; then
  echo "Creating screenshots directory..."
  mkdir -p "$SCREENSHOTS_DIR"
fi

# Build the application
echo "Building application..."
dotnet build

# Run the CLI application with the provided arguments
echo "Running CLI application..."
dotnet run -- "$@"

# Display screenshots if they exist and authenticate command was used
if [[ "$*" == *"authenticate"* ]] && [ -d "$SCREENSHOTS_DIR" ] && [ "$(ls -A $SCREENSHOTS_DIR)" ]; then
  echo "Screenshots were captured during authentication. They are available in the $SCREENSHOTS_DIR directory."
  ls -la "$SCREENSHOTS_DIR"
fi

echo "Done!"
