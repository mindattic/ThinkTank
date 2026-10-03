Deploy the ThinkTank Blazor app via **MindAttic.Deploy** (sibling repo at `D:\Projects\MindAttic\MindAttic.Deploy`).

Run this command and report the result:

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "cd D:\Projects\MindAttic\MindAttic.Deploy; npm run deploy -- --app thinktank"
```

The app entry (`MindAttic.Deploy/projects.json` -> `apps[]` slug `thinktank`) is currently **disabled** pending a workflow + Azure infra, so today this prints the "disabled" note and exits 0. See `.claude/skills/deploy/SKILL.md` for the steps to enable it.

Notes:
- There is no landing page to deploy: mindattic.com has no ThinkTank page and MindAttic.Deploy rejects `--only thinktank`. This repo's README on GitHub -- https://github.com/mindattic/ThinkTank -- is the project page; edit `README.md` and push to `main` to update it.
