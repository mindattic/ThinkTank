// Cypress UI flow for the Call-Vote dialog on /thinktank.
//
// What's covered:
//   - Setup panel exposes participant pills and the Start button
//   - Toggling a participant pill flips aria-pressed
//   - With 2+ participants selected, starting a conversation reveals the
//     Call Vote button in the chat header
//   - Clicking Call Vote opens the dialog with all three vote types
//   - Switching to "What direction next?" reveals the comma-separated
//     options input
//   - Cancelling closes the dialog and resets VotePending
//
// What's NOT covered (matches the limits called out in chat.cy.js):
//
//   - The vote itself never runs. Submitting would dispatch real LLM calls
//     through MindAttic.Legion server-side; cy.intercept can't stub that
//     path because it lives in-process on the server. The C# unit suite
//     (VotingServiceTests + SettingsServiceVaultOverlayTests) covers the
//     ChatParticipant→VoterProfile mapping and the credential plumbing.
//   - Starting the conversation may fire LLM dispatch in the background.
//     The chat header (and thus Call Vote) appears synchronously when
//     ActiveState.Running flips true, so the dialog test does NOT depend
//     on whether the LLM call succeeds. In a test environment without
//     real API keys, the call fails fast and an error bubble renders.

const blazorClick = ($el) => {
  // Bypasses Cypress's actionability checks for cases where the Blazor circuit
  // has @onclick on a parent / overlapping element. Document-level dispatch
  // works regardless of visual layering — same pattern used in settings.cy.js.
  const evt = new MouseEvent('click', { bubbles: true, cancelable: true, view: window });
  $el[0].dispatchEvent(evt);
};

const VOTE_DIALOG = '[role="dialog"][aria-labelledby="vote-dialog-title"]';

describe('/thinktank — Call Vote dialog', () => {
  beforeEach(() => {
    cy.visit('/thinktank', { failOnStatusCode: false });
    // Wait for the Blazor Server interactive circuit to bind @onclick handlers.
    cy.wait(2500);

    // The /thinktank page reuses any existing tab from a prior session. Force
    // a fresh setup state by clicking "+ New" so each test starts from an
    // empty conversation regardless of prior persistence.
    cy.get('.chat-tab.add', { timeout: 15000 }).then(blazorClick);
    cy.get('.setup-panel', { timeout: 15000 }).should('be.visible');
  });

  it('setup panel renders participant pills and the Start button', () => {
    cy.get('.participant-pill').its('length').should('be.greaterThan', 1);
    cy.contains('button.start-btn', /^\s*Start\s*$/).should('exist');
  });

  it('clicking a participant pill flips aria-pressed to true', () => {
    cy.get('.participant-pill').not(':contains("+ From library")').first().then(($el) => {
      blazorClick($el);
    });
    cy.get('.participant-pill[aria-pressed="true"]').its('length').should('be.gte', 1);
  });

  it('opens the vote dialog when 2 participants are selected and Start is clicked', () => {
    cy.get('#setup-topic').type('Cypress vote-dialog smoke');

    // Select the first two non-library participant pills.
    cy.get('.participant-pill').not(':contains("+ From library")').then(($pills) => {
      blazorClick(Cypress.$($pills[0]));
      blazorClick(Cypress.$($pills[1]));
    });

    cy.get('.participant-pill[aria-pressed="true"]').its('length').should('be.gte', 2);

    // Start the conversation. We don't await an LLM response — Running flips
    // synchronously and the chat header (with Call Vote) renders immediately.
    cy.contains('button.start-btn', /^\s*Start\s*$/).then(blazorClick);

    cy.contains('button', /Call Vote|Voting…/, { timeout: 15000 }).should('be.visible');

    cy.contains('button', /Call Vote/).then(blazorClick);

    cy.get(VOTE_DIALOG, { timeout: 10000 })
      .should('be.visible')
      .within(() => {
        cy.contains(/Have we reached consensus\?/).should('exist');
        cy.contains(/What is our conclusion\?/).should('exist');
        cy.contains(/What direction next\?/).should('exist');

        // Default question text matches Consensus type.
        cy.get('#vote-question').should('have.value', 'Have we reached consensus?');
      });
  });

  it('switching to "What direction next?" reveals the options input', () => {
    cy.get('#setup-topic').type('Direction-vote test');
    cy.get('.participant-pill').not(':contains("+ From library")').then(($pills) => {
      blazorClick(Cypress.$($pills[0]));
      blazorClick(Cypress.$($pills[1]));
    });
    cy.contains('button.start-btn', /^\s*Start\s*$/).then(blazorClick);
    cy.contains('button', /Call Vote|Voting…/, { timeout: 15000 }).then(blazorClick);

    cy.get(VOTE_DIALOG, { timeout: 10000 }).within(() => {
      cy.get('#vote-direction-options').should('not.exist');

      cy.get('input[type="radio"][name="voteType"]').eq(2).check({ force: true });

      cy.get('#vote-direction-options', { timeout: 5000 }).should('be.visible');
      cy.get('#vote-question').should('have.value', 'What direction should we take next?');
    });
  });

  it('Cancel button closes the dialog', () => {
    cy.get('#setup-topic').type('Cancel-test topic');
    cy.get('.participant-pill').not(':contains("+ From library")').then(($pills) => {
      blazorClick(Cypress.$($pills[0]));
      blazorClick(Cypress.$($pills[1]));
    });
    cy.contains('button.start-btn', /^\s*Start\s*$/).then(blazorClick);
    cy.contains('button', /Call Vote|Voting…/, { timeout: 15000 }).then(blazorClick);

    cy.get(VOTE_DIALOG, { timeout: 10000 }).should('be.visible');

    cy.get(VOTE_DIALOG).contains('button', /Cancel/).then(blazorClick);

    cy.get(VOTE_DIALOG).should('not.exist');
  });
});
