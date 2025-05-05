#!/usr/bin/env node

/**
 * ADFS Authentication CLI
 * 
 * This script is a command-line interface for the ADFS authentication module.
 * It accepts a configuration file path as an argument and runs the authentication process.
 */

const fs = require('fs');
const { authenticate } = require('./adfs-auth');

// Check if a configuration file path is provided
if (process.argv.length < 3) {
  console.error('Usage: node adfs-auth-cli.js <config-file>');
  process.exit(1);
}

const configFile = process.argv[2];

// Read the configuration file
try {
  const configJson = fs.readFileSync(configFile, 'utf8');
  const config = JSON.parse(configJson);
  
  // Run the authentication
  authenticate(config)
    .then(result => {
      // Output the result as JSON for the .NET library to parse
      console.log(JSON.stringify(result));
      process.exit(0);
    })
    .catch(error => {
      console.error(`Error: ${error.message}`);
      // Output a JSON error for the .NET library to parse
      console.log(JSON.stringify({
        success: false,
        error: error.message
      }));
      process.exit(1);
    });
} catch (error) {
  console.error(`Error reading or parsing config file: ${error.message}`);
  process.exit(1);
}
