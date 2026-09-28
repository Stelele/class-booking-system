// ─────────────────────────────────────────────────────────────────────────────
// class-booking — Pulumi deploy program
//
// Target: an EXISTING DigitalOcean droplet (nginx already installed), driven
// entirely over SSH from the machine running this program (the GitHub Actions
// runner in CI, see .github/workflows/deploy.yml).
//
// Resource chain (each DependsOn the previous):
//
//   write-ssh-key → write-env → deploy → nginx-vhost
//
//   write-ssh-key : decode DO_SSH_KEY (base64) → /tmp/do_key on the runner
//   write-env     : build /opt/class-booking/.env locally, scp it (0600);
//                   secrets never enter a command string, a log, or state
//   deploy        : scp docker-compose.yml → `docker compose pull && up -d`,
//                   retaining the running build's hashed chunks across the swap
//                   so an in-flight tab's lazy route imports still resolve
//   nginx-vhost   : vhost proxying {domain} → 127.0.0.1:8081, nginx reload
//
// write-ssh-key and deploy carry a per-run trigger (runStamp): the CI runner is
// ephemeral, so the key must be re-materialized and images re-pulled on every
// `pulumi up`, even when Pulumi sees no config change. write-env re-runs only
// when the .env content changes (SHA-256 fingerprint trigger).
//
// TLS — do this ONCE, manually, after the first successful `pulumi up`:
//
//   ssh <user>@<host>
//   certbot --nginx -d <domain>      # edits the vhost + enables auto-renew
//
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Pulumi;
using Pulumi.Command.Local;

// aliases so call sites can say LocalCommand without a namespace qualifier
using LocalCommand = Pulumi.Command.Local.Command;
using LocalCommandArgs = Pulumi.Command.Local.CommandArgs;

return await Deployment.RunAsync(() =>
{
    // ── inputs ──────────────────────────────────────────────────────────────

    // DOMAIN env (CI) wins; Pulumi stack config is the local fallback — the
    // ephemeral-state CI stack reliably fails to carry yaml config across runs
    var domain = Environment.GetEnvironmentVariable("DOMAIN")
        ?? new Config().Get("class-booking:domain")
        ?? throw new InvalidOperationException(
            "Domain missing — set DOMAIN env or class-booking:domain config.");

    // Guard against path/command injection via the domain (it is embedded in
    // remote file paths and the command string below).
    var domainPattern = @"^(?=.{1,253}$)([a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,63}$";
    if (!Regex.IsMatch(domain, domainPattern, RegexOptions.CultureInvariant))
        throw new InvalidOperationException($"'{domain}' is not a valid domain name.");

    string RequireEnv(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Environment variable '{name}' is required.");
        return value.Trim();
    }

    var host      = RequireEnv("DO_HOST");
    var user      = Environment.GetEnvironmentVariable("DO_USER") is { Length: > 0 } u ? u.Trim() : "root";
    var sshKeyB64 = RequireEnv("DO_SSH_KEY");
    var ghOwner   = RequireEnv("GH_OWNER").ToLowerInvariant(); // ghcr image paths are lowercase

    // Optional passthrough keys → /opt/class-booking/.env (consumed by docker
    // compose; see infra/docker-compose.yml).
    var optionalKeys = new[]
    {
        "FIXED_MEET_LINK", "MEET_PROVIDER", "GOOGLE_CLIENT_ID", "GOOGLE_CLIENT_SECRET",
        "GOOGLE_REDIRECT_URI", "GOOGLE_LOGIN_REDIRECT_URI", "GOOGLE_TOKEN_KEY",
        "R2_ACCOUNT_ID", "R2_KEY_ID", "R2_SECRET", "R2_BUCKET",
                "EMAIL_API_KEY", "EMAIL_FROM",
        "PUSH_VAPID_PUBLIC_KEY", "PUSH_VAPID_PRIVATE_KEY", "PUSH_VAPID_SUBJECT",
        "TEACHER_EMAIL", "STUDENT_A_EMAIL", "STUDENT_B_EMAIL",
    };

    var envLines = new List<string> { $"GH_OWNER={ghOwner}" };
    foreach (var key in optionalKeys)
    {
        if (Environment.GetEnvironmentVariable(key) is not { } value || string.IsNullOrWhiteSpace(value))
            continue;
        // strip CR/LF so a multiline secret cannot corrupt the .env file
        envLines.Add($"{key}={value.Trim().Replace("\r", "").Replace("\n", "")}");
    }
    var envContent = string.Join("\n", envLines) + "\n";

    // Uniform ssh/scp invocations: key at /tmp/do_key, accept-new host keys.
    var keyFile = "/tmp/do_key";
    var sshOpts = $"-i {keyFile} -o StrictHostKeyChecking=accept-new";
    string Ssh(string remoteCmd) => $"ssh {sshOpts} {user}@{host} '{remoteCmd}'";
    string Scp(string localPath, string remotePath) => $"scp {sshOpts} {localPath} {user}@{host}:{remotePath}";

    var runStamp = DateTimeOffset.UtcNow.ToString("O"); // changes every program run

    // ── resource 1: write-ssh-key ───────────────────────────────────────────
    var sshKey = new LocalCommand("write-ssh-key", new LocalCommandArgs
    {
        Create = $"echo {sshKeyB64} | base64 -d > {keyFile} && chmod 600 {keyFile}",
        Interpreter = { "/bin/bash", "-c" },
        Triggers = { { "runStamp", runStamp } }, // runner is ephemeral → always re-run
    });

    // ── resource 2: write-env ───────────────────────────────────────────────
    // The .env is built locally and scp'd; its content never appears in any
    // command string, so it is never logged and never stored in Pulumi state.
    // The only trace is a SHA-256 fingerprint used as the change trigger.
    // The local temp file is created 0600 and removed by a shell trap.
    var envPath = Path.Combine(Path.GetTempPath(), $"class-booking-env-{Guid.NewGuid():N}.tmp");
    if (!Deployment.Instance.IsDryRun)
    {
        File.WriteAllText(envPath, envContent);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(envPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    var envHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(envContent)));

    var writeEnv = new LocalCommand("write-env", new LocalCommandArgs
    {
        Create = string.Join(" && ",
            $"trap 'rm -f {envPath}' EXIT",
            Ssh("mkdir -p /opt/class-booking"),
            Scp(envPath, "/opt/class-booking/.env"),
            Ssh("chmod 600 /opt/class-booking/.env")),
        Interpreter = { "/bin/bash", "-c" },
        Triggers = { { "envHash", envHash } },
    }, new CustomResourceOptions { DependsOn = { sshKey } });

    // ── resource 3: deploy ──────────────────────────────────────────────────
    var composePath = Path.GetFullPath("docker-compose.yml"); // CWD = infra/
    var deployScript = """
        set -euo pipefail
        cd /opt/class-booking
        SVC=frontend
        STASH=/opt/class-booking/retained-assets
        KEEP=3

        docker compose pull

        # Each deploy replaces the whole dist, so every content-hashed chunk
        # name changes. A tab still running the previous build asks for exactly
        # the names we are about to delete, and its lazy route import then dies.
        # Stash the running build's chunks first and re-inject them after the
        # swap, so an in-flight tab finishes its navigation instead of 404ing.
        # Keyed by image id: a rebuild that produced identical output must not
        # evict assets the running build is still serving.
        mkdir -p "$STASH"
        if running=$(docker compose ps -q "$SVC" 2>/dev/null) && [ -n "$running" ]; then
          # ':' would make the stash path ambiguous to `docker cp` (src:dst)
          image=$(docker inspect -f '{{.Image}}' "$running" | tr -c 'A-Za-z0-9' '-')
          if [ -n "$image" ]; then
            rm -rf "$STASH/$image"
            mkdir -p "$STASH/$image"
            docker cp "$running:/usr/share/nginx/html/assets/." "$STASH/$image/" || true
          fi
        fi

        docker compose up -d

        # Serve the retained builds next to the new one. A file that a later
        # build deleted stays reachable, so the served set converges to the
        # union of the retained generations rather than shrinking — a few KB
        # per renamed chunk, in exchange for no in-flight tab ever 404ing.
        # Best-effort: a failed injection costs one reload for that tab, and
        # must never fail the deploy itself.
        if [ -d "$STASH" ]; then
          if fresh=$(docker compose ps -q "$SVC" 2>/dev/null) && [ -n "$fresh" ]; then
            for dir in "$STASH"/*/; do
              [ -d "$dir" ] || continue
              docker cp "$dir." "$fresh:/usr/share/nginx/html/assets/" || true
            done
          fi
          # bound the stash: retention is only needed across one or two deploys
          ls -1dt "$STASH"/*/ 2>/dev/null | tail -n +$((KEEP + 1)) | xargs -r rm -rf || true
        fi
        """;

    var deploy = new LocalCommand("deploy", new LocalCommandArgs
    {
        Create = string.Join(" && ",
            Scp(composePath, "/opt/class-booking/docker-compose.yml"),
            $"{Ssh("bash -s")} <<'DEPLOY_EOF'\n{deployScript}\nDEPLOY_EOF"),
        Interpreter = { "/bin/bash", "-c" },
        Triggers = { { "runStamp", runStamp } }, // re-pull :latest on every up
    }, new CustomResourceOptions { DependsOn = { writeEnv } });

    // ── resource 3b: enable Google Calendar API (optional, no-op when unset) ─
    // Skipped entirely unless GCP_PROJECT_ID + GCP_CREDENTIALS are provided
    // (see docs/superpowers/specs/gcp-console-checklist.md). Requires gcloud on
    // the runner; enablement is idempotent, so ephemeral state is safe.
    var gcpProject = Environment.GetEnvironmentVariable("GCP_PROJECT_ID");
    var gcpCreds = Environment.GetEnvironmentVariable("GCP_CREDENTIALS");
    if (!string.IsNullOrWhiteSpace(gcpProject) && !string.IsNullOrWhiteSpace(gcpCreds))
    {
        var credFile = $"{runStamp}-gcp.json";
        new LocalCommand("gcp-enable-calendar-api", new LocalCommandArgs
        {
            Create = string.Join(" && ",
                $"printf '%s' \\\"$GCP_CREDENTIALS\\\" > /tmp/{credFile}",
                $"CLOUDSDK_CORE_PROJECT={gcpProject} GOOGLE_APPLICATION_CREDENTIALS=/tmp/{credFile} " +
                "gcloud services enable calendar-json.googleapis.com --quiet",
                $"rm -f /tmp/{credFile}"),
            Interpreter = { "/bin/bash", "-c" },
        }, new CustomResourceOptions { DependsOn = { sshKey } });
    }

    // ── resource 4: nginx-vhost ─────────────────────────────────────────────
    // Quoted heredocs (<<'EOF') keep $host / $proxy_add_x_forwarded_for /
    // $scheme LITERAL — nothing expands on the runner or on the droplet.
    // The outer heredoc feeds the remote script to `ssh … 'bash -s'` via
    // stdin, avoiding single-quote nesting entirely.
    //
    // TLS: the 443 block is included whenever the certbot cert exists
    // (checked on the droplet at apply time). WITHOUT this, every deploy
    // would stomp certbot's 443 config back to HTTP-only and Cloudflare
    // (Full mode) would fall through to the droplet's default vhost —
    // i.e. the radius site taking over the lessons domain. Cert renewals
    // only refresh cert FILES, so this static template stays valid.
    var nginxConf = $@"server {{
    listen 80;
    server_name {domain};

    location /.well-known/acme-challenge/ {{ root /var/www/html; }}
    location / {{ return 301 https://$host$request_uri; }}
}}
";
    // appended by the remote script only when the cert exists (see below)
    var tlsConf = $@"
server {{
    listen 443 ssl;
    server_name {domain};

    ssl_certificate /etc/letsencrypt/live/{domain}/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/{domain}/privkey.pem;

    location / {{
        proxy_pass http://127.0.0.1:8081;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }}
}}
";
    var vhostFile   = $"/etc/nginx/sites-available/{domain}";
    var enabledLink = $"/etc/nginx/sites-enabled/{domain}";

    var remoteScript =
        $"cat > {vhostFile} <<'EOF'\n" +
        nginxConf +
        "EOF\n" +
        // TLS block only when certbot has issued the cert — a fresh droplet
        // must not fail nginx -t on missing cert files (chicken-and-egg)
        $"if [ -d /etc/letsencrypt/live/{domain} ]; then\n" +
        $"  cat >> {vhostFile} <<'EOF'\n" +
        tlsConf +
        "EOF\n" +
        "fi\n" +
        $"ln -sf {vhostFile} {enabledLink}\n" +
        "nginx -t && systemctl reload nginx";

    new LocalCommand("nginx-vhost", new LocalCommandArgs
    {
        Create = $"{Ssh("bash -s")} <<'NGINX_EOF'\n{remoteScript}\nNGINX_EOF",
        Interpreter = { "/bin/bash", "-c" },
    }, new CustomResourceOptions { DependsOn = { deploy } });

    // ── outputs ─────────────────────────────────────────────────────────────
    return new Dictionary<string, object?>
    {
        ["siteUrl"] = $"https://{domain}",
        // cert issuance (once per droplet): certbot --nginx -d {domain}
        // renewals keep the same cert FILES, so this vhost template stays valid
        ["tlsNote"] = $"If /etc/letsencrypt/live/{domain} is absent, run once on the droplet: certbot --nginx -d {domain}",
    };
});
