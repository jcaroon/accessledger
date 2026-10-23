# API and authorization

The browser and API share an origin. Protected routes require the signed session cookie. Every POST additionally requires the current `X-CSRF-TOKEN` from `GET /api/session` and its matching antiforgery cookie. Refresh the token after starting a workspace or changing personas.

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/health` | Database connectivity status; no data returned |
| GET | `/api/session` | Current identity and antiforgery token |
| POST | `/api/demo/start` | Create a private fictional workspace and sign in as requester |
| POST | `/api/demo/persona` | Switch to a supported role within the same workspace |
| GET | `/api/resources` | Fictional resource catalog and allowed access levels |
| GET | `/api/requests?search=&status=` | Role-scoped list and unfiltered visible totals |
| GET | `/api/requests/{id}` | Scoped request detail and audit history |
| POST | `/api/requests` | Create a pending request; auditors forbidden |
| POST | `/api/requests/{id}/decision` | Independent approver decision with expected version |
| POST | `/api/requests/{id}/cancel` | Withdraw a request owned by the signed-in person |
| GET | `/api/audit` | Audit records for requests visible to the current identity |
| GET | `/openapi/v1.json` | Machine-readable API document |

## Request creation

```json
{
  "title": "Review the delivery plan",
  "resourceKey": "project-atlas",
  "scope": "Read",
  "reason": "I need to review the project plan before the delivery handoff."
}
```

Titles must have 6–100 characters; reasons need 15–2,000. `resourceKey` and `scope` must be in the server catalog. Ownership, status, timestamps, and version are assigned by the server.

## Decision

```json
{
  "decision": "Approved",
  "note": "Read-only access matches the documented business need.",
  "expectedVersion": 1
}
```

The note needs 10–2,000 characters. Valid decisions are `Approved` and `Rejected`. A non-pending or stale request produces 409. An attempt to decide one's own request produces 403. Requests outside the caller's visible scope produce 404.

## Role matrix

| Action | Requester | Approver | Auditor |
|---|---|---|---|
| Read requests | Own only | Workspace | Workspace |
| Submit request | Yes | Yes | No |
| Decide request | No | Others' pending requests | No |
| Withdraw request | Own pending only | Own pending only | No |
| Read audit | Own visible requests | Workspace | Workspace |
| Edit audit records | No | No | No |

List results are bounded to 100 requests; audit responses to the newest 200 visible events. A demo workspace accepts at most 50 requests. Starting new workspaces is rate-limited.
