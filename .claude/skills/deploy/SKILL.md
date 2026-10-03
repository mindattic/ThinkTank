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
- There is no landing page: mindattic.com has no ThinkTank page and MindAttic.Deploy rejects `--only thinktank`. This repo's README on GitHub (https://github.com/mindattic/ThinkTank) is the project page. This `/deploy` command is for the APP only.
