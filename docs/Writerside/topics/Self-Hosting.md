# Self-Hosting

The web dashboard and the API that the apps upload to are one ASP.NET Core app, shipped as the Docker image
`ghcr.io/the-onyx-team/onyx:latest` (built from `master`).

## With Docker Compose and SQL Server

```bash
cp .env.example .env        # set MSSQL_SA_PASSWORD, mail and optional OAuth logins
docker compose up -d
```

- The app listens on `127.0.0.1:8080` (change with `ONYX_BIND` / `ONYX_PORT` in `.env`). Put a TLS-terminating reverse proxy (Caddy, nginx, Traefik) in front of it.
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
| `ConnectionStrings__mail` | `smtp://host:port` (no TLS) or `smtps://user:password@host:587` (STARTTLS, password URL-encoded) for confirmation and password reset emails. Without it no emails are sent. |
| `Mail__From` | Sender address for those emails. Use one your SMTP server may send as. |
| `Onyx__AllowRegistration` | `true` (default) or `false`. Off blocks new accounts (password and external login); existing users can still sign in. |
| `KeyDirectory` | Where the signing and data-protection keys are kept (`/app/data` in the image). |
| `Authentication__{Google,Microsoft,GitHub}__ClientId` / `__ClientSecret` | Optional external logins; each is enabled only when both values are set. |

The container's health check calls `/alive`.

## Connecting the apps

- **Android / Windows:** the login and register pages show the server and a **Change** button. The app checks
  that `/alive` answers before it saves the address. A build can also start with your server preselected, see
  [Android App](Android-App.md).
- **Linux:** `onyx-agent login --server https://onyx.example.com`.
- **Web:** open the server's address in a browser.

## Private server behind a VPN

The apps only need to reach the server; nothing has to be public. The web dashboard needs **HTTPS**, because
its sign-in cookie is `Secure`. Browsers only accept that over plain HTTP on `localhost`.

| Setup | Server | App build |
|---|---|---|
| **Tailscale** (simplest) | `ONYX_BIND` unchanged, then `tailscale serve --bg 8080`. The server is at `https://<machine>.<tailnet>.ts.net` with a trusted certificate. | Standard |
| **WireGuard + own domain** | DNS name pointing at the server's VPN address, reverse proxy (e.g. Caddy) with a Let's Encrypt certificate via DNS challenge. | Standard |
| **WireGuard + plain HTTP** | `ONYX_BIND=<VPN address>` (e.g. `10.8.0.1`). The dashboard is reached through `ssh -L 8080:localhost:8080 server` at `http://localhost:8080`. | `--private-network` |
| **Own CA** (e.g. Caddy `tls internal`) | Install the CA certificate on the phone and on the PC. | `--private-network` |

On the phone, turn on **Always-on VPN** for the VPN app. Otherwise Android's background upload (every
30 minutes) fails while the VPN is down. It retries later and loses nothing, since Android keeps the usage history.
