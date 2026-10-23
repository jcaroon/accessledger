# Security boundaries

AccessLedger is a fictional, single-instance portfolio demonstration. It does not connect to an organization or grant real access.

Implemented boundaries include protected authentication cookies, antiforgery validation on mutations, server-side role and ownership checks, per-workspace data scoping, self-approval rejection, transactional audit writes, optimistic concurrency, bounded input and list sizes, and rate-limited workspace creation. The public demo uses HTTPS on a separate hostname; its cookies are HTTP-only, SameSite, and `__Host-` scoped in Production.

The deliberate demo persona-switch endpoint is **not** an organizational authorization system. It only changes role inside the visitor's own fictional workspace. Disable and replace demo endpoints before considering real organizational data or provisioning.

Audit records cannot be changed through this API. Administrators with filesystem or database access can change the database, so the application does not claim cryptographic tamper evidence or regulatory compliance.

Data Protection keys are unencrypted at rest in the local deployment. Restrict the runtime data directory and protect backups. A deployment requiring encrypted key storage should configure a suitable external protector.

The Caddy deployment trusts forwarded HTTPS information from loopback only; do not expose the internal backend listener publicly. Do not add wildcard credentialed CORS policies. All runtime data, keys, environment files, and build caches are excluded from the source repository.

Report a reproducible issue without posting credentials or personal data. Do not use the public demo to store real confidential information.
