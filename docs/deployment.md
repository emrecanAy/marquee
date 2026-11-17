# Deployment

CI runs on every push and pull request. CD is split in two so that half of it is useful before
any server exists.

| Stage | When it runs | Needs |
|---|---|---|
| Publish images to GHCR | A tag `v*` is pushed | Nothing: uses the built-in `GITHUB_TOKEN` |
| Deploy to the server | After a publish, or manually, **only if** `DEPLOY_ENABLED` is `true` | The settings below |

Until `DEPLOY_ENABLED` is set, the deploy job is skipped and the workflow stays green.

## Turning deployment on

1. **Prepare the server** (any Linux host with Docker and the Compose plugin):

   ```bash
   sudo mkdir -p /opt/marquee && sudo chown "$USER" /opt/marquee
   cat > /opt/marquee/.env <<'ENV'
   POSTGRES_PASSWORD=<long random value>
   IDENTITY_SIGNING_KEY=<at least 32 random characters>
   IDENTITY_ENCRYPTION_KEY=<at least 32 random characters, different from the signing key>
   ADMIN_EMAIL=<the first administrator's e-mail>
   ADMIN_PASSWORD=<at least 12 characters; change it after the first sign-in>
   ENV
   chmod 600 /opt/marquee/.env
   ```

   If the images are private, log the server in once: `docker login ghcr.io`. The site must be served over HTTPS
   (a reverse proxy in front of the `web` container): the session cookie is sent only over a secure connection.

2. **Create a deploy key** and authorise it on the server:

   ```bash
   ssh-keygen -t ed25519 -f marquee_deploy -N ""
   ssh-copy-id -i marquee_deploy.pub deploy@your-server
   ssh-keyscan your-server        # value for DEPLOY_KNOWN_HOSTS
   ```

3. **Add the settings** under *Settings > Secrets and variables > Actions*:

   | Kind | Name | Value |
   |---|---|---|
   | Secret | `DEPLOY_HOST` | Server address |
   | Secret | `DEPLOY_USER` | SSH user |
   | Secret | `DEPLOY_SSH_KEY` | Contents of `marquee_deploy` |
   | Secret | `DEPLOY_KNOWN_HOSTS` | Output of `ssh-keyscan` (pins the host key) |
   | Variable | `DEPLOY_URL` | Public base URL, used for the post-deploy health check |
   | Variable | `DEPLOY_ENABLED` | `true` |

4. **Release**: `git tag v1.0.0 && git push origin v1.0.0`.

To roll back, run the CD workflow manually with an earlier tag.

## What the deploy job does

It copies `deploy/compose.prod.yaml` to the server, pulls the images for the tag, runs
`docker compose up --detach` and then polls `/health` until the new version answers.

## Notes for a real production setup

- **TLS.** The web container speaks plain HTTP on port 80. Put a reverse proxy that terminates
  TLS in front of it (Caddy, Traefik, or a cloud load balancer).
- **Migrations.** The API applies its EF Core migrations at start-up, which is fine for one
  instance. Before scaling out, run them once per release instead (for example with an EF
  migration bundle as a separate deploy step) and set `Database__MigrateOnStartup=false`.
- **Background workers.** The outbox processors, the expiry sweep and the waiting-room dispatcher
  use `SKIP LOCKED` or atomic Redis scripts, so they are safe to run on every instance.
- **Change data capture** is not part of `compose.prod.yaml`. A production setup would use a
  managed Kafka and run Debezium next to it with the connector in `deploy/debezium/`.
