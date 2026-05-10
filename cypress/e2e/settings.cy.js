// Cypress assertions for /settings — covers all three tabs (Personas, Defaults,
// Appearance) plus the SettingsAppearance child component. No live LLM calls
// are made; these are UI-rendering assertions only.

describe('/settings — Personas/Defaults/Appearance', () => {
  beforeEach(() => {
    cy.visit('/settings', { failOnStatusCode: false });
  });

  it('renders the three settings tabs', () => {
    cy.get('#tab-personas',   { timeout: 15000 }).should('be.visible').and('contain.text', 'Personas');
    cy.get('#tab-defaults').should('be.visible').and('contain.text', 'Defaults');
    cy.get('#tab-appearance').should('be.visible').and('contain.text', 'Appearance');
  });

  it('Personas tab is active by default and shows the persona list', () => {
    cy.get('#tab-personas').should('have.attr', 'aria-selected', 'true');
    cy.get('#panel-personas').within(() => {
      cy.contains('h2', /Personas/).should('exist');
      cy.contains('button', /\+ Add Custom Persona/).should('exist');
      // Default templates are seeded on first launch — at least one persona
      // row should always be present.
      cy.get('button.template-row').its('length').should('be.greaterThan', 0);
    });
  });

  it('Defaults tab shows global tokens / rounds inputs and the fallback toggle', () => {
    cy.get('#tab-defaults').click();
    cy.get('#panel-defaults').within(() => {
      cy.get('#defaults-max-tokens')
        .should('have.attr', 'min', '64')
        .and('have.attr', 'max', '32768');
      cy.get('#defaults-max-rounds')
        .should('have.attr', 'min', '1')
        .and('have.attr', 'max', '999');
      cy.get('#defaults-claude-fallback').should('have.attr', 'type', 'checkbox');
      cy.contains(/Shared credentials file/i).should('exist');
    });
  });

  it('Appearance tab exposes theme selector + 3 sliders with documented ranges', () => {
    cy.get('#tab-appearance').click();
    cy.get('#panel-appearance').within(() => {
      cy.get('#appearance-theme option').its('length').should('eq', 18);

      cy.get('#appearance-control-height')
        .should('have.attr', 'type', 'range')
        .and('have.attr', 'min', '28')
        .and('have.attr', 'max', '60');

      cy.get('#appearance-gutter')
        .should('have.attr', 'min', '0')
        .and('have.attr', 'max', '30');

      cy.get('#appearance-border-radius')
        .should('have.attr', 'min', '0')
        .and('have.attr', 'max', '24');
    });
  });

  it('switching theme updates html[data-theme] via JS interop', () => {
    cy.get('#tab-appearance').click();
    cy.get('#appearance-theme').select('matrix');
    cy.get('html').should('have.attr', 'data-theme', 'matrix');
  });
});
