// Headless navigation smoke for the ThinkTank Blazor Server app.
// Visits each top-level route, asserts the Blazor circuit boots without an
// error banner, and verifies the top-nav links rendered by NavMenu.razor.
// Runs against CYPRESS_BASE_URL (default http://localhost:5100).

const routes = [
  { path: '/',          name: 'Home',          marker: /Open Think Tank/i },
  { path: '/thinktank', name: 'Conversations', marker: /\+ New|Conversation/i },
  { path: '/settings',  name: 'Settings',      marker: /Personas|Defaults|Appearance/ },
];

const failurePhrases = [
  /An unhandled error has occurred/i,
  /An error has occurred. This application may no longer respond/i,
  /Sorry, something went wrong/i,
];

function assertNoErrorBanner() {
  cy.document().its('body').then(($body) => {
    const text = $body.innerText || '';
    failurePhrases.forEach((rx) => {
      expect(rx.test(text), `should not show "${rx}"`).to.be.false;
    });
  });
}

describe('ThinkTank navigation smoke', () => {
  it('home page boots and shows entry buttons', () => {
    cy.visit('/');
    cy.contains(/Open Think Tank/i, { timeout: 15000 }).should('be.visible');
    cy.contains('a', /Settings/).should('be.visible');
    assertNoErrorBanner();
  });

  routes.forEach((r) => {
    it(`loads ${r.name} (${r.path})`, () => {
      cy.visit(r.path, { failOnStatusCode: false });
      cy.contains(r.marker, { timeout: 15000 }).should('exist');
      assertNoErrorBanner();
    });
  });

  it('top-nav exposes Home, Conversations, Settings', () => {
    cy.visit('/');
    cy.get('nav.top-nav').within(() => {
      cy.contains('a', /^Home$/).should('exist');
      cy.contains('a', /Conversations/).should('exist');
      cy.contains('a', /Settings/).should('exist');
    });
  });

  it('NotFound route renders gracefully (no raw 500)', () => {
    cy.visit('/this-route-does-not-exist', { failOnStatusCode: false });
    cy.contains(/not found|404/i, { timeout: 15000 }).should('exist');
    assertNoErrorBanner();
  });
});
