---
name: commit
description: Stage, commit, push, and print the commit hash. Use /commit for auto-message or /commit "Message" for custom.
---

When invoked:

1. Run `git status` and `git diff --stat` to see what changed
2. Stage changed tracked files (use specific filenames, not `git add -A`)
   - Before committing, log the work as a ticket with the `workflow` CLI from inside the repo:
     `workflow ticket new --type <feature|fix|chore|docs> --status closed --summary "..." --ref "..."`,
     or `workflow ticket close <CODE-Tn>` for an open ticket it finishes; then `workflow ticket verify`.
     Stage `docs/USER_STORIES.md` with the rest.
3. **If arguments were provided** (`$ARGUMENTS`), use that as the commit message
4. **If no arguments**, auto-generate a descriptive commit message summarizing the "why" not the "what"
5. Always append to the commit message: `Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>`
6. Commit and push to remote
7. Print the commit hash and message
8. Always end with: `To revert: /revert <hash>`
