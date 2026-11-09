# Self-Hosting

The web dashboard and the API that the apps upload to are one ASP.NET Core app, shipped as the Docker image
`ghcr.io/the-onyx-team/onyx:latest` (built from `master`).

## With Docker Compose and SQL Server

```bash
cp .env.example .env        # set MSSQL_SA_PASSWORD, mail and optional OAuth logins
docker compose up -d
```

- The app listens on `127.0.0.1:8080`. Put a TLS-terminating reverse proxy (Caddy, nginx, Traefik) in front of it.
  The app trusts `X-Forwarded-Proto` and `X-Forwarded-For` from it, which it needs for correct links in emails
  and for per-client rate limiting.
- The `onyx_data` volume holds the JWT signing key and the ASP.NET data-protection keys. **Back it up and never
  delete it:** losing it signs out every user and every device.
- SQL Server is not reachable from outside the compose network. Back up the `onyx_sqlserver_data` volume.
- Migrations run automatically when the app starts.

## With SQLite

Without `provider=SqlServer`, the image uses SQLite at `/app/data/onyx.db`, in the same volume as the keys:

```bash
docker run -d -p 127.0.0.1:8080:8080 -v onyx_data:/app/data -e ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
  ghcr.io/the-onyx-team/onyx:latest
```

## Configuration

| Setting | Purpose |
|---|---|
| `provider` | `SQLite` (default) or `SqlServer` |
| `ConnectionStrings__SQLite` / `ConnectionStrings__SqlServer` | Database connection. The app refuses to start if the selected provider has none. |
| `ConnectionStrings__mail` | `smtp://host:port` for confirmation and password reset emails. Without it no emails are sent. |
| `KeyDirectory` | Where the signing and data-protection keys are kept (`/app/data` in the image). |
| `Authentication__{Google,Microsoft,GitHub}__ClientId` / `__ClientSecret` | Optional external logins; each is enabled only when both values are set. |

The container's health check calls `/alive`.
