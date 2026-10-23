# Deployment

The public demonstration uses a dedicated hostname, `accessledger.jsjaon.com`, separate from the portfolio invitation manager. This is an intentionally public fictional application. Do not put real organization data into it.

## Production host

1. Build the frontend with `cd web && npm ci && npm run build`.
2. Publish a self-contained Linux build: `dotnet publish server -c Release -r linux-x64 --self-contained true -o publish`.
3. Deploy it to a versioned release under `/opt/accessledger/releases/`; point `/opt/accessledger/current` to that release.
4. Create a dedicated non-login `accessledger` user. Install the included systemd service. Its runtime directory `/var/lib/accessledger` holds the SQLite database and protected-cookie key material; it must not be served by the web server.
5. Add the included Caddy hostname block to the existing configuration, validate it, and reload Caddy. Keep the backend bound to loopback. Caddy manages HTTPS.
6. Verify `/api/health`, workspace creation, independent decision, read-only auditing, and a second browser's isolation.

The default ASP.NET Core forwarded-header configuration only trusts loopback proxy addresses. Production authentication and antiforgery cookies require HTTPS and use the `__Host-` prefix. Avoid adding a blanket CSP at the reverse proxy; the application already emits a restrictive policy.

The application applies its checked-in EF migration on startup. Runtime data and keys persist across application releases. Back up the database using SQLite's backup API, along with the keys directory, rather than copying a live database file without its write-ahead log.

## Docker

The image runs as the .NET image's non-root application user. Local Compose maps only loopback port 5080 and uses Development mode for local HTTP. For public hosting, use Production mode behind a trusted HTTPS reverse proxy and retain the named data volume.

## Verification limits

The repository includes a Dockerfile and Compose configuration. The release is compiled and tested directly with the .NET SDK and Node; Docker execution requires a working local Docker daemon. Do not infer that an unexecuted container build was verified merely because the native build passes.
