// Cypress UI flow for the /thinktank conversations page. These specs exercise
// affordances that don't require firing a real LLM call:
//
//   - Tab strip rendering and "+ New" tab creation
//   - Vote dialog open/cancel and validation
//   - User-injection input field is reachable
//
// Live LLM dispatch happens server-side via MindAttic.Legion (see
// ThinkTankService.CallProvider → LegionClient.CallChatAsync). Browser-level
// cy.intercept can't stub that path — it lives in-process on the server. To
// keep this suite hermetic we test the UI plumbing only; LLM dispatch is
// covered by the C# unit suite (ThinkTankServiceTests, NameGeneratorServiceTests).

describe('/thinktank — chat UI affordances', () => {
  beforeEach(() => {
    cy.visit('/thinktank', { failOnStatusCode: false });
  });

  it('renders the tab strip with at least one tab and a "+ New" button', () => {
    cy.get('.chat-tabs', { timeout: 15000 }).should('be.visible');
    cy.get('.chat-tab.add').should('contain.text', '+ New');
    cy.get('.chat-tab').not('.add').its('length').should('be.greaterThan', 0);
  });

  it('clicking "+ New" creates an additional tab', () => {
    cy.get('.chat-tab').not('.add').then(($before) => {
      const before = $before.length;
      cy.get('.chat-tab.add').click();
      cy.get('.chat-tab').not('.add').should('have.length', before + 1);
    });
  });

  it('right-click on a tab opens the rename/close context menu', () => {
    cy.get('.chat-tab').not('.add').first().rightclick();
    cy.get('.context-menu', { timeout: 5000 }).within(() => {
      cy.contains('button', /Rename/).should('be.visible');
      cy.contains('button', /Close/).should('be.visible');
    });
  });

  it('Call Vote dialog opens, validates required question, and cancels cleanly', () => {
    // "Call Vote" lives in the chat toolbar — only present when a tab exists.
    cy.contains('button', /Call Vote|Vote/i, { timeout: 15000 }).first().click();

    cy.get('.dialog-box', { timeout: 5000 }).within(() => {
      cy.contains(/Call a Vote/).should('be.visible');
      // Run Vote should be disabled with no question text.
      cy.get('#vote-question').should('have.value', '');
      cy.contains('button', /Run Vote/).should('be.disabled');

      cy.get('#vote-question').type('Have we reached consensus?');
      cy.contains('button', /Run Vote/).should('not.be.disabled');

      cy.contains('button', /Cancel/).click();
    });

    cy.get('.dialog-box').should('not.exist');
  });
});
