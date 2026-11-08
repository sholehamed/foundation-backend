# FR-12G-C — Private cross-repository CI smoke

Date: 2026-11-08. Prerequisite for joint Angular + Identity + BFF browser E2E.

## Access model
Both repositories remain private. The Backend repository Actions Secret `FOUNDATION_FRONTEND_READ_TOKEN` is a Fine-grained PAT with only `Contents: Read-only` on `sholehamed/foundation-frontend`. The source and value must never appear in workflow logs, code, chat, or artifacts.

## Trusted event boundary
The workflow `.github/workflows/foundation-cross-repo-smoke.yml` executes only for a trusted main push that changes the workflow itself, or `workflow_dispatch` on main. It has no PR trigger. The job checks exact repository and `refs/heads/main`; its `GITHUB_TOKEN` has only `contents: read`. Both `actions/checkout` steps use `persist-credentials: false`, the Frontend checkout pins `ref: main`, and no untrusted code from the second repository is executed.

The CI job confirms presence of the Backend Web project and Frontend Angular/Playwright project and logs only the two commit SHAs; a failed second checkout proves missing/invalid/expired token or insufficient access. GitHub Actions cannot read the Secret plaintext after registration. If the token expires, replace the repository Actions Secret.

## Still pending
A green checkout is NOT a live OAuth/OIDC browser integration. Continue with the next stage: ephemeral SQL Server and genuine HTTPS Identity + Tenant origins, Chromium, BFF Authorization Code + PKCE callback, HttpOnly Cookie, CSRF and Tenant host isolation. Existing FR-12F real bearer HTTP + SQL and FR-12G Angular mocked Chromium/Backend TestServer remain separate evidence. Never enable the pilot in production without real integrated acceptance.

Master tracker: Backend issue #44.