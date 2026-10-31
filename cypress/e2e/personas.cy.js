// Cypress assertions for /personas (LAMP, TT-T27): the persona editor is ThinkTank content and stays in the
// app; the old Defaults/Appearance tabs and the key fields moved to MindAttic Vault (the Settings cog).
// No live LLM calls are made; these are UI-rendering assertions only.

describe('/personas', () => {
  beforeEach(() => {
    cy.visit('/personas', { failOnStatusCode: false });
    // Wait for the Blazor Server circuit to finish hydrating before asserting.
    cy.wait(2500);
  });

  it('shows the persona list', () => {
    cy.get('#panel-personas', { timeout: 15000 }).within(() => {
      cy.contains('h2', /Personas/).should('exist');
      cy.contains('button', /\+ Add Custom Persona/).should('exist');
      cy.get('button.template-row').its('length').should('be.greaterThan', 0);
    });
  });

  it('no longer hosts any app settings', () => {
    cy.get('#tab-defaults').should('not.exist');
    cy.get('#tab-appearance').should('not.exist');
    cy.get('input[type=password]').should('not.exist');
  });
});
