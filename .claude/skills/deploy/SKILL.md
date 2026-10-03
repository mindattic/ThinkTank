---
name: deploy
description: Deploy ThinkTank via MindAttic.Deploy (sibling repo). Fires the GitHub Actions workflow that targets the thinktank Azure App Service. Currently DISABLED in MindAttic.Deploy -- no workflow exists yet, and no Azure infrastructure is provisioned.
---

When invoked, run:

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "cd D:\Projects\MindAttic\MindAttic.Deploy; npm run deploy -- --app thinktank"
```

Report the result. Today this prints the "disabled" note and exits 0. To enable:
1. Add `.github/workflows/azure-deploy.yml` mirroring StreetSamurai's pattern (push-to-main trigger).
2. Provision a `thinktank` Azure App Service.
3. Add `AZURE_WEBAPP_PUBLISH_PROFILE` secret to `mindattic/ThinkTank`.
4. Flip `apps[].disabled` from `true` to `false` in `MindAttic.Deploy/projects.json`.

Notes:
- The README-driven landing page (`mindattic.com/thinktank.htm`) that the legacy `scripts/cli/deploy.{bat,ps1}` + `build-html.js` used to ship -- and later MindAttic.Deploy's catalog mode (`--only thinktank`) -- is retired (MindAttic.Deploy DEP-A6, 2026-10-03). This repo's README on GitHub (https://github.com/mindattic/ThinkTank) is the project page. This `/deploy` command is for the APP only.
