#!/usr/bin/env node

/**
 * ADFS Authentication Automation
 * 
 * This script automates the ADFS authentication flow and retrieves access tokens.
 * It uses Puppeteer to control a browser, navigate to the ADFS login page,
 * fill in credentials, and capture the resulting tokens.
 */

const puppeteer = require('puppeteer');
const fs = require('fs');
const path = require('path');

/**
 * Generate a random string for state and nonce parameters
 */
function generateRandomString(length) {
  const chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
  let result = '';
  for (let i = 0; i < length; i++) {
    result += chars.charAt(Math.floor(Math.random() * chars.length));
  }
  return result;
}

/**
 * Build the authorization URL with all required parameters
 */
function buildAuthorizationUrl(config, state, nonce) {
  // Make sure the authority has a valid URL format
  let authority = config.authority || 'https://auth.integ.alliedpilots.org';
  if (!authority.startsWith('http://') && !authority.startsWith('https://')) {
    authority = 'https://' + authority;
  }
  
  console.log(`Original authority URL: ${authority}`);
  
  // We need to ensure we have the correct URL structure for ADFS
  // The correct URL should be like: https://auth.integ.alliedpilots.org/adfs/oauth2/authorize/
  
  // First check if authority already ends with /adfs/ (from .parameters file)
  let authUrl;
  if (authority.toLowerCase().endsWith('/adfs/')) {
    // Authority already has /adfs/ at the end, so just add oauth2/authorize/
    authUrl = `${authority}oauth2/authorize/`;
  } else if (authority.toLowerCase().includes('/adfs')) {
    // Has /adfs somewhere but not at the end in the correct format
    // Extract the base URL and reconstruct
    const baseUrl = authority.substring(0, authority.toLowerCase().indexOf('/adfs'));
    authUrl = `${baseUrl}/adfs/oauth2/authorize/`;
  } else {
    // No /adfs in the URL, add it
    authUrl = `${authority}/adfs/oauth2/authorize/`;
  }
  console.log(`Constructed auth URL: ${authUrl}`);
  
  const url = new URL(authUrl);
  
  // Make sure clientId is set
  const clientId = config.clientId || 'https://sentinel.alliedpilots.org';
  url.searchParams.append('client_id', clientId);
  
  // Make sure redirectUri is set
  const redirectUri = config.redirectUri || 'https://sentinel.integ.alliedpilots.org/redirect';
  url.searchParams.append('redirect_uri', redirectUri);
  
  url.searchParams.append('response_type', 'id_token token');
  url.searchParams.append('scope', config.scope || 'openid offline_access');
  url.searchParams.append('nonce', nonce);
  url.searchParams.append('state', state);
  url.searchParams.append('prompt', 'select_account');
  return url.toString();
}

/**
 * Extract tokens from the redirect URL
 */
function extractTokensFromUrl(url) {
  try {
    // Parse the URL
    const parsedUrl = new URL(url);
    
    // The tokens are in the fragment part of the URL (after the #)
    const fragment = parsedUrl.hash.substring(1);
    if (!fragment) {
      return { success: false, error: 'No fragment in URL' };
    }
    
    // Parse the fragment into key-value pairs
    const params = new URLSearchParams(fragment);
    const accessToken = params.get('access_token');
    const idToken = params.get('id_token');
    const error = params.get('error');
    const errorDescription = params.get('error_description');
    
    if (error) {
      return {
        success: false,
        error,
        errorDescription
      };
    }
    
    if (!accessToken) {
      return { success: false, error: 'No access token found in URL' };
    }
    
    return {
      success: true,
      accessToken,
      idToken,
      tokenType: params.get('token_type'),
      refreshToken: params.get('refresh_token')
    };
  } catch (error) {
    return { success: false, error: error.message };
  }
}

/**
 * Authenticate with ADFS and get tokens
 */
async function authenticate(config) {
    console.log('Starting ADFS authentication process...');
    
    // Validate required parameters
    if (!config.username || !config.password) {
        return {
            success: false,
            error: 'Missing credentials',
            errorDescription: 'Username and password are required for authentication'
        };
    }
    
    if (!config.authority || !config.clientId || !config.redirectUri) {
        return {
            success: false,
            error: 'Missing configuration',
            errorDescription: 'Authority, clientId, and redirectUri are required for authentication'
        };
    }
    
    const browser = await puppeteer.launch({
        headless: config.headless === false ? false : 'new',
        slowMo: config.slowMo || 0,
        args: ['--no-sandbox', '--disable-setuid-sandbox', '--disable-dev-shm-usage'],
        defaultViewport: null
    });
    
    try {
        const page = await browser.newPage();
        
        // Set default navigation timeout
        page.setDefaultNavigationTimeout(config.timeout || 60000);
        
        // Enable request interception to capture tokens
        await page.setRequestInterception(true);
        
        // Store tokens when found in URL
        let authTokens = null;
        
        // Listen for requests to capture tokens
        page.on('request', async (request) => {
            const url = request.url();
            
            // Check if this is the redirect URL with tokens
            if (url.startsWith(config.redirectUri) && 
                (url.includes('id_token=') || url.includes('access_token='))) {
                
                console.log(`Found tokens in redirect URL: ${url}`);
                
                // Parse tokens from URL
                const urlObj = new URL(url);
                const hashParams = new URLSearchParams(urlObj.hash.substring(1));
                
                authTokens = {
                    idToken: hashParams.get('id_token'),
                    accessToken: hashParams.get('access_token'),
                    tokenType: hashParams.get('token_type'),
                    expiresIn: hashParams.get('expires_in'),
                    scope: hashParams.get('scope'),
                    state: hashParams.get('state')
                };
                
                // Take screenshot if enabled
                if (config.screenshots && config.screenshots.enabled) {
                    const screenshotDir = config.screenshots.directory || 'screenshots';
                    const screenshotPrefix = config.screenshots.prefix || 'auth_';
                    const timestamp = new Date().toISOString().replace(/[:.]/g, '-');
                    const screenshotPath = `${screenshotDir}/${screenshotPrefix}tokens_${timestamp}.png`;
                    
                    try {
                        await page.screenshot({ path: screenshotPath, fullPage: true });
                        console.log(`Captured screenshot: ${screenshotPath}`);
                    } catch (err) {
                        console.error(`Error capturing screenshot: ${err.message}`);
                    }
                }
            }
            
            // Continue with the request
            request.continue();
        });
        
        // Log page console messages
        page.on('console', msg => {
            console.log(`PAGE LOG: ${msg.text()}`);
        });
        
        // Capture navigation events for debugging
        page.on('framenavigated', frame => {
            if (frame === page.mainFrame()) {
                console.log(`Navigated to: ${frame.url()}`);
            }
        });
        
        // Log all requests for debugging
        page.on('request', request => {
            console.log(`Request: ${request.method()} ${request.url()}`);
        });
        
        // Build the authorization URL using our helper function
        // which properly handles authority URLs that may already contain /adfs/
        // and adds all required parameters
        const state = generateRandomString(16);
        const nonce = generateRandomString(16);
        const authUrlString = buildAuthorizationUrl(config, state, nonce);
        
        console.log(`Navigating to ADFS login page: ${authUrlString}`);
        
        // Navigate to the ADFS login page
        await page.goto(authUrlString, { waitUntil: 'networkidle2' });
        
        // Take screenshot if enabled
        if (config.screenshots && config.screenshots.enabled) {
            const screenshotDir = config.screenshots.directory || 'screenshots';
            const screenshotPrefix = config.screenshots.prefix || 'auth_';
            const timestamp = new Date().toISOString().replace(/[:.]/g, '-');
            const screenshotPath = `${screenshotDir}/${screenshotPrefix}login_${timestamp}.png`;
            
            try {
                await page.screenshot({ path: screenshotPath, fullPage: true });
                console.log(`Captured screenshot: ${screenshotPath}`);
            } catch (err) {
                console.error(`Error capturing screenshot: ${err.message}`);
            }
        }
        
        // Get the HTML content for debugging
        const html = await page.content();
        console.log(`Page HTML length: ${html.length}`);
        
        // Find and fill the username field
        const usernameSelector = '#userNameInput';
        await page.waitForSelector(usernameSelector, { timeout: config.timeout || 60000 });
        console.log(`Found username field with selector: ${usernameSelector}`);
        await page.type(usernameSelector, config.username);
        console.log(`Entering username: ${config.username}`);
        
        // Find and fill the password field
        const passwordSelector = '#passwordInput';
        await page.waitForSelector(passwordSelector, { timeout: config.timeout || 60000 });
        console.log(`Found password field with selector: ${passwordSelector}`);
        await page.type(passwordSelector, config.password);
        console.log('Entering password');
        
        // Find and click the sign-in button
        const submitSelector = '#submitButton';
        await page.waitForSelector(submitSelector, { timeout: config.timeout || 60000 });
        console.log(`Found submit button with selector: ${submitSelector}`);
        await page.click(submitSelector);
        console.log('Clicking sign-in button');
        
        // Take screenshot after login if enabled
        if (config.screenshots && config.screenshots.enabled) {
            const screenshotDir = config.screenshots.directory || 'screenshots';
            const screenshotPrefix = config.screenshots.prefix || 'auth_';
            const timestamp = new Date().toISOString().replace(/[:.]/g, '-');
            const screenshotPath = `${screenshotDir}/${screenshotPrefix}after_login_${timestamp}.png`;
            
            try {
                // Wait a bit for the page to process
                await page.waitForTimeout(2000);
                await page.screenshot({ path: screenshotPath, fullPage: true });
                console.log(`Captured screenshot: ${screenshotPath}`);
            } catch (err) {
                console.error(`Error capturing screenshot: ${err.message}`);
            }
        }
        
        // Wait for redirect to the callback URL
        console.log(`Waiting for redirect to: ${config.redirectUri}`);
        
        try {
            // Set a timeout for waiting for the redirect
            const redirectTimeoutPromise = new Promise((_, reject) => {
                setTimeout(() => {
                    reject(new Error(`Timeout waiting for redirect to ${config.redirectUri}`));
                }, config.timeout || 60000);
            });
            
            // Wait for navigation to the redirect URI
            const navigationPromise = page.waitForNavigation({
                timeout: config.timeout || 60000,
                waitUntil: 'networkidle2'
            });
            
            // Race between timeout and navigation
            await Promise.race([navigationPromise, redirectTimeoutPromise]);
            
            // Check if we have tokens already captured from the request interception
            if (authTokens) {
                console.log('Authentication successful, tokens captured from URL');
                
                // Write tokens to output file if specified
                if (config.outputFile) {
                    const result = {
                        success: true,
            if (authTokens) {
                console.log('Authentication successful despite redirect timeout, tokens captured from URL');
                
                // Write tokens to output file if specified
                if (config.outputFile) {
                    const result = {
                        success: true,
                        tokens: authTokens
                    };
                    
                    fs.writeFileSync(config.outputFile, JSON.stringify(result, null, 2));
                    console.log(`Tokens written to ${config.outputFile}`);
                }
                
                return {
                    success: true,
                    tokens: authTokens
                };
            }
            
            // Take screenshot of the current state if enabled
            if (config.screenshots && config.screenshots.enabled) {
                const screenshotDir = config.screenshots.directory || 'screenshots';
                const screenshotPrefix = config.screenshots.prefix || 'auth_';
                const timestamp = new Date().toISOString().replace(/[:.]/g, '-');
                const screenshotPath = `${screenshotDir}/${screenshotPrefix}error_${timestamp}.png`;
                
                try {
                    await page.screenshot({ path: screenshotPath, fullPage: true });
                    console.log(`Captured error screenshot: ${screenshotPath}`);
                } catch (err) {
                    console.error(`Error capturing screenshot: ${err.message}`);
                }
            }
            
            return {
                success: false,
                error: 'Timeout',
                errorDescription: error.message
            };
        }
    } catch (error) {
        console.error(`Authentication error: ${error.message}`);
        console.error(error.stack);
        
        return {
            success: false,
            error: 'Authentication error',
            errorDescription: error.message
        };
    } finally {
        // Close the browser
        await browser.close();
        console.log('Browser closed');
    }
}

module.exports = { authenticate };
