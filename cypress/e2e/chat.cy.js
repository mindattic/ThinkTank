// Cypress UI flow for the /thinktank conversations page. These specs exercise
// affordances that don't require firing a real LLM call:
//
//   - Tab strip rendering and "+ New" tab creation
//   - Right-click context menu for rename/close
//
// Live LLM dispatch happens server-side via MindAttic.Legion (see
// ThinkTankService.CallProvider → LegionClient.CallChatAsync). Browser-level
// cy.intercept can't stub that path — it lives in-process on the server. To
// keep this suite hermetic we test the UI plumbing only; LLM dispatch is
// covered by the C# unit suite (ThinkTankServiceTests, NameGeneratorServiceTests).
//
// The Call-Vote dialog isn't tested here because the button is disabled
// until a tab has 2+ participants (Chat.razor:348), and seeding participants
// would require driving the Personas UI as part of setup.

describe('/thinktank — chat UI affordances', () => {
  beforeEach(() => {
    cy.visit('/thinktank', { failOnStatusCode: false });
    // Wait for the Blazor Server interactive circuit to bind @onclick
    // handlers; without this, the first click races hydration.
    cy.wait(2500);
  });

  it('renders the tab strip with at least one tab and a "+ New" button', () => {
    cy.get('.chat-tabs', { timeout: 15000 }).should('be.visible');
    cy.get('.chat-tab.add').should('contain.text', '+ New');
    cy.get('.chat-tab').not('.add').its('length').should('be.greaterThan', 0);
  });

  it('clicking "+ New" creates an additional tab', () => {
    cy.get('.chat-tab').not('.add').then(($before) => {
      const before = $before.length;
      cy.get('.chat-tab.add').then(($el) => {
        const evt = new MouseEvent('click', { bubbles: true, cancelable: true, view: window });
        $el[0].dispatchEvent(evt);
      });
      cy.get('.chat-tab', { timeout: 10000 }).not('.add').should('have.length', before + 1);
    });
  });

  it('right-click on a tab opens the rename/close context menu', () => {
    cy.get('.chat-tab').not('.add').first().rightclick();
    cy.get('.context-menu', { timeout: 10000 }).within(() => {
      cy.contains('button', /Rename/).should('be.visible');
      cy.contains('button', /Close/).should('be.visible');
    });
  });
});
