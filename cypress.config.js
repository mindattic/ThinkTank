const { defineConfig } = require('cypress');

// ThinkTank.Blazor binds to http://localhost:5100 (https://localhost:7100) per
// ThinkTank.Blazor/Properties/launchSettings.json. Override with CYPRESS_BASE_URL
// when running against a different port.
module.exports = defineConfig({
  e2e: {
    baseUrl: process.env.CYPRESS_BASE_URL || 'http://localhost:5100',
    specPattern: 'cypress/e2e/**/*.cy.js',
    supportFile: false,
    video: false,
    screenshotOnRunFailure: true,
    defaultCommandTimeout: 15000,
    pageLoadTimeout: 60000,
    responseTimeout: 60000,
    requestTimeout: 30000,
    viewportWidth: 1600,
    viewportHeight: 900,
    chromeWebSecurity: false,
  },
});
