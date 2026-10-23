# AccessLedger

**The right access. A clear record.**

An original full-stack portfolio project by **jcaroon**: request access to a fictional resource, review the business reason as an independent approver, and inspect the server-recorded audit trail.

[Live demo](https://accessledger.jsjaon.com) · [Architecture](docs/architecture.md) · [API and authorization](docs/api.md) · [Security boundaries](SECURITY.md)

![AccessLedger request workspace](docs/screenshots/workspace.png)

## Try the complete workflow

1. Choose **Start a private demo**. The server creates a separate sample workspace for your browser session.
2. As **Requester**, submit a request with a resource, access level, and business reason.
3. Switch **View as** to **Approver**, select the request, and approve or reject it with a decision note.
4. Switch to **Auditor** and inspect the history. Writing is blocked by the API, not just the interface.
5. Try approving an approver's own request, or submitting a stale decision: the server refuses both.

No real accounts, permissions, Microsoft tenants, or external services are connected. All names and records are fictional. The demo role switch is intentionally available within your own workspace; it is **not** a production sign-in mechanism.

## What the project demonstrates

- **Frontend:** React, TypeScript, responsive layouts, accessible native dialogs, keyboard interaction, search/filter states, validation, and error recovery.
- **API:** ASP.NET Core, signed-cookie authentication, antiforgery validation, authorization and ownership checks, rate limiting, and consistent problem responses.
- **Persistence:** Entity Framework Core, SQLite, checked-in migrations, transactional request/audit updates, indexed workspace-scoped queries, and optimistic concurrency.
- **Verification:** integration tests against the real HTTP pipeline and SQLite, including cross-workspace access, role bypass, self-approval, simultaneous decisions, and expired sessions.
- **Delivery:** a multistage Dockerfile, local Compose setup, GitHub Actions, and a deployable backend that serves the compiled frontend.

## Run with Docker

```sh
docker compose up --build
```

Open **http://localhost:5080**. Compose uses Development mode for local HTTP; the Docker image defaults to Production for an HTTPS reverse proxy. The database and authentication keys live in the named `accessledger-data` volume. There are no external credentials to configure.

## Run from source

Prerequisites: **.NET SDK 10** and **Node.js 22+**.

```sh
# Terminal 1: API
ASPNETCORE_ENVIRONMENT=Development dotnet run --project server --urls http://127.0.0.1:5080

# Terminal 2: frontend (the dev server proxies /api to the API)
cd web
npm ci
npm run dev
```

Open **http://localhost:5174**. On PowerShell, set `$env:ASPNETCORE_ENVIRONMENT="Development"` before running the API command. Use a single hostname consistently so browser cookies remain attached to the same development origin.

To serve the compiled frontend from ASP.NET Core:

```sh
cd web
npm ci
npm run build
cd ..
ASPNETCORE_ENVIRONMENT=Development dotnet run --project server --urls http://127.0.0.1:5080
```

## Verify

```sh
dotnet test tests/AccessLedger.Tests.csproj
cd web
npm ci
npm run build
npx playwright install chromium
npm test
```

GitHub Actions runs the frontend build, API integration suite, browser workflow checks, and release publication. The test suite creates fictional workspaces and isolated temporary SQLite stores; it never uses the live demo database.

## Data and lifecycle

A browser receives an HTTP-only session cookie. Every person, request, and audit event belongs to a workspace; all application reads and writes enforce that boundary. Workspaces expire after 24 hours and are removed by a cleanup job that runs every 30 minutes. The sign-in cookie expires after 12 hours. **Reset demo** creates a new workspace; the previous one remains until cleanup.

The default local data directory is `server/App_Data/`, which is ignored by Git. Set `DataDirectory` to a writable directory for deployment. Keep the SQLite store and Data Protection key directory together when backing up an environment. See [deployment notes](docs/deployment.md).

## Deliberate limits

This project models a request-and-review workflow. It does not integrate with Microsoft Graph or perform real provisioning. Audit records are append-only through this application's API; they are not a tamper-proof external ledger. A production rollout would require organizational OIDC sign-in, explicit tenant membership, removal of demo persona switching, operational monitoring, and a separately authorized provisioning process.

SQLite keeps the demo easy to run and inspect. The EF model and concurrency boundary are portable, but moving to PostgreSQL or SQL Server requires provider-specific migrations and verification; it is not a claimed one-line production switch.

## Repository map

```text
server/                 ASP.NET Core API and compiled frontend host
  Domain/               Request, workspace, person, and audit models
  Data/                 DbContext and checked-in database migrations
  Services/             Demo workspace creation and expiry cleanup
tests/                  HTTP integration tests with SQLite
web/                    React + TypeScript frontend
docs/                   Architecture, API, and deployment notes
.github/workflows/      Build and test automation
```

MIT licensed. This project contains original sample code and fictional data, with no employer source code or confidential business information.
