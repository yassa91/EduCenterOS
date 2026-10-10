# EduCenterOS

Backend foundation for education center management. Sprint S01 provides the local API host, safe runtime configuration, PostgreSQL, isolated tests and CI. S02 is complete: IdentityAccess persistence, security configuration and local OTP delivery are active; the full request/resend/verify/register account flow is active. Frontend and production deployment remain outside this sprint.

## Prerequisites

.NET SDK from `global.json`, Python 3.11+, Git, PostgreSQL client (`psql` 17+), OpenSSL for explicit Development RSA provisioning, Infisical CLI 0.43.120.
No local database or Docker required. Development and Integration tests use two distinct Supabase
projects; Unit and Architecture tests require no database credentials.

## Supabase setup

See [T41](docs/technical/T41%20-%20Supabase%20Development%20%26%20Testing.md) for ownership, permissions and secrets.
Current projects: Development `hwuqjxbcbpbsdbonpdpf`; Testing `fvrvmmkxtifxslwunlko`.
Get the exact direct/session host and server major from each project's dashboard. Connections use
5432 and certificate/hostname verification; transaction pooler 6543 is rejected.

```sh
infisical login --domain=https://app.infisical.com/api
python3 scripts/dev.py trust --endpoint https://app.infisical.com/api --project-id 4243f1e5-ad83-4044-9160-cf1819e70d90
python3 scripts/dev.py trust-database --environment Development --project-reference hwuqjxbcbpbsdbonpdpf --host aws-0-eu-west-1.pooler.supabase.com --server-major 17 --root-certificate "$HOME/.config/EduCenterOS/supabase-ca.crt"
python3 scripts/dev.py trust-database --environment Testing --project-reference fvrvmmkxtifxslwunlko --host aws-1-eu-central-1.pooler.supabase.com --server-major 17 --root-certificate "$HOME/.config/EduCenterOS/supabase-ca.crt"
```

Download the public CA from the dashboard's Database Settings → SSL configuration → Download certificate,
and save it at `~/.config/EduCenterOS/supabase-ca.crt` before trusting the targets. The reviewed public CA
is also in `infra/supabase-ca.crt` for CI; its SHA256 is recorded in T41.

Trusted non-secret locators live outside Git in `~/.config/EduCenterOS`. Add only the project's
existing database password as `SUPABASE_DATABASE_PASSWORD` in each respective Infisical scope:
`/backend-api/supabase-development` and `/backend-api/supabase-testing` (both in Infisical `dev`).
Do not paste passwords into chat, Git or command arguments. Keep Supabase Data API disabled.

```sh
python3 scripts/dev.py provision-cloud --environment Development
python3 scripts/dev.py provision-cloud --environment Testing
dotnet restore EduCenterOS.sln --locked-mode
dotnet build EduCenterOS.sln -c Release --no-restore
dotnet dev-certs https --trust
python3 scripts/dev.py provision-auth
python3 scripts/dev.py identity-migrate
python3 scripts/dev.py run
```

The API binds to `127.0.0.1:5100` for existing registration/diagnostics and `https://localhost:5101` for the approved browser authentication protocol. HTTPS uses the trusted local .NET development certificate. Ctrl+C stops it; Supabase remains running.
`/health/live` and `/health/ready` support GET/HEAD and return safe Healthy/Unhealthy status.
Business API description: `/openapi/v1.json`. API startup does not apply migrations.
Development OTP delivery remains the protected local mailbox described below.

In Development, open `https://localhost:5101/swagger` for Swagger UI. It uses the existing `/openapi/v1.json` document and provides **Try it out** for the active operations. The UI assets are served locally. Rebuild and restart the API after code changes. Swagger UI is not enabled in Testing, Staging or Production.

### macOS double-click launcher

After the initial machine setup above, `EduCenterOS.app` runs development without a terminal. It shows startup progress, connects to Supabase, performs locked restore and Release build, applies IdentityAccess migrations through the separate reviewed migrator, and opens Swagger after liveness and readiness succeed. It uses the same `scripts/dev.py` bootstrap and Infisical account; it never provisions or replaces secrets automatically.

The **Stop application** button or closing the launcher stops the API process it started. Supabase stays available. If EduCenterOS is already running, the launcher opens its Swagger without rebuilding or stopping that existing instance. To pick up code edits, stop the existing API before launching again. An occupied port belonging to another application produces an error and is left untouched.

The app is built locally and remembers this checkout's absolute path. Moving the checkout requires rebuilding it. To recreate it with the macOS Command Line Tools installed:

```sh
python3 scripts/build_macos_launcher.py
```

The default generated app is `.local/mac-launcher/EduCenterOS.app`; an explicit `--output /absolute/path/EduCenterOS.app` selects another location. Generated app bundles contain no secrets and are not committed.

## Verify

For a guide to the implemented features, shared code, and tests, see the
[code map](docs/technical/Code%20Map.md).

The sole formatting authority is
[educenteros-formatting](.agents/skills/educenteros-formatting/SKILL.md).
Its formatter is exposed through the existing project commands:

```sh
dotnet run --project tools/EduCenterOS.Formatting -c Release -- --fix
dotnet run --project tools/EduCenterOS.Formatting -c Release -- --check
```

```sh
python3 scripts/verify.py
```

The gate runs locked restore, Release build, bootstrap/report guard tests and all three .NET suites.
On a workstation it fetches restricted test credentials from Infisical. CI uses GitHub secret
`EDUCENTEROS_TEST_DATABASE_SNAPSHOT`, derived from the trusted test bundle; never development,
bootstrap/admin or migration credentials. Restore/build/Unit/Architecture receive no cloud credentials.
Integration tests serialize access to the separately marked test project and clean only their
reviewed schemas. Missing credentials/connection/ownership or skipped tests fail the gate.

For database-independent verification after restore/build:

```sh
dotnet test tests/EduCenterOS.UnitTests/EduCenterOS.UnitTests.csproj -c Release --no-build
dotnet test tests/EduCenterOS.ArchitectureTests/EduCenterOS.ArchitectureTests.csproj -c Release --no-build
```

The legacy local PostgreSQL volume is retained until cloud acceptance and explicit disposal.

## Phone verification and account registration (S02)

The approved Development sender writes temporary messages to `.local/otp/<challengeId>.json`. The application creates owner-only directories (0700) and files (0600), rejects symbolic links and a second host sharing the mailbox, and deletes messages after verification/invalidation/expiry and graceful shutdown. There is no OTP-read API or raw OTP log. Request/resend/verify routes are active; see [the contract](docs/contracts/S02-registration.md). Each issue response supplies a challenge ID; open its message file locally and submit its code to Verify. A successful Verify deletes the file and returns a temporary proof once. Use that proof and challenge ID in `POST /api/v1/accounts` with `fullName`, `password` and optional `emailAddress`; the phone comes from the verified challenge. Registration returns account/person IDs and consumes the proof atomically. It does not log in or grant institution access. Real SMS is outside this sprint.

The flow below can be exercised in a local HTTP client against `http://127.0.0.1:5100`. Use the request-body editor for credentials and disable saved request/response history and body logging. Placeholders below are not real codes or credentials; do not put a password or proof in a URL, shell command argument, Git, screenshots or shared logs.

1. POST `/api/v1/phone-verifications`, body `{"phoneNumber":"01012345678"}`. Read `challengeId` from the 200 response, then open `.local/otp/<challengeId>.json` locally to obtain its temporary `code`. No SMS is sent.
2. POST `/api/v1/phone-verifications/<challengeId>/verify`, body `{"code":"<six ASCII digits from the local file>"}`. A 200 response supplies `verificationProof` and `expiresAtUtc`; the message file is deleted. Keep the proof only long enough to complete this flow.
3. POST `/api/v1/accounts` with the body below. Supply the challenge ID from step 1 and proof from step 2; choose a 12–128 UTF-16 unit password without control characters. Omit/null `emailAddress` when not needed.

```json
{
  "challengeId": "<challenge UUID>",
  "verificationProof": "<temporary proof>",
  "fullName": "اسم المستخدم",
  "password": "<your password>",
  "emailAddress": null
}
```

A 201 response returns only `userAccountId`, `personIdentityId`, and `createdAtUtc`. There is no login token or account GET URL. Reusing a consumed/expired/wrong proof returns 422; a verified duplicate contact returns a generic 409. Invalid fields return 400 with `errors` keyed by `body.<field>`. Respect 429 and any `Retry-After` supplied; a 500 does not establish whether a commit happened and must not trigger blind replay.

To replace a code/proof, wait at least the issue response's `resendAvailableAtUtc`, then POST `/api/v1/phone-verifications/<challengeId>/resend` with `{}`. Use the **new** ID/file; the old code and unused proof become invalid. Codes/proofs each last 300 seconds; expiry is inclusive. Three issues and ten verification attempts per phone in a persistent 900-second window are the local baseline; resend/restart does not reset these budgets. Graceful stop, invalidation and expiry also remove mailbox messages.

`infra/registration-policy.json` contains the reviewed local numeric baselines; the launcher captures all fields in the immutable startup snapshot. Invalid/missing fields or keys fail startup. A persisted key fingerprint blocks changing the partition key in a way that resets durable target quotas; do not delete that binding to bypass policy. Rotation needs a reviewed transition. Staging/Production remain disabled.

## Diagnostics and delivery

- Missing/conflicting environment, invalid startup configuration or missing snapshot fail before readiness. Staging/Production require T39 and are currently disabled.
- If dev bootstrap fails, check CLI version/login/project access and Supabase target/certificate availability. Scripts hide raw provider output and never print secret values. Do not bypass validation by copying a dev connection string into appsettings.
- Each verification run has a unique ignored directory under `artifacts/test-results/`. Raw local logs/TRX are private diagnostics and may contain failed-test input; review them before sharing. CI uploads only an allowlisted JSON projection (static test names, outcomes and counts), without stdout, attachments, paths or failure text, for seven days.
- CI `verify` runs on every PR into `main`, pushes to `main` and manual dispatch. A failure remains a failure; there are no automatic retries or required skips.
- Each task uses a new branch from updated `main`, review and Squash Merge according to [T40](docs/technical/T40%20-%20Git%20%26%20GitHub%20Workflow.md). Automatic merge is delegated after acceptance, checks and review. The current private-repository plan blocks GitHub branch protection; these gates are enforced procedurally until server-side protection is available.

The approved registration scope and current delivery evidence are in [S02](docs/sprints/S02.md) and [the registration contract](docs/contracts/S02-registration.md). Foundation evidence is in [S01](docs/sprints/S01.md). Current cloud hosting is defined by [T41](docs/technical/T41%20-%20Supabase%20Development%20%26%20Testing.md). Historical runtime, tests and CI contracts are in [T37](docs/technical/T37%20-%20Local%20Runtime%20%26%20Docker.md), [T33](docs/technical/T33%20-%20Testing%20Stack.md) and [T38](docs/technical/T38%20-%20CI%20%26%20Verification.md).

## Authentication runtime (S03)

The immutable runtime snapshot is now schema4 (maximum 65,536 UTF-8 bytes). Old or partial snapshots fail startup. Reviewed numeric policy is in `infra/authentication-policy.json`; signing material is held only in the existing Development Infisical scope. `provision-auth` explicitly adds a separate RSA key when the entire authentication extension is absent, validates an existing complete extension, and refuses partial configuration. It preserves existing DB/OTP/partition secrets. The protected YAML input is removed immediately; raw CLI output is withheld. CI and test hosts generate their own RSA keys, with a distinct synthetic issuer/origin.

JWT key rotation requires prepublishing the new public key in the validation map, activating the matching new private key/current ID, retaining the old public key until every old token has expired plus the explicit skew, and then retiring it. Keep one to four immutable lowercase key IDs; never overwrite an existing ID with different public material. There is no token-directed discovery or automatic key replacement. OS TLS certificates and JWT signing keys are separate. Authentication routes become available with their owning S03 tasks; see [the contract](docs/contracts/S03-authentication.md).

Login is active at `POST /api/v1/auth/login`. After S02 registration, use HTTPS Swagger with `phoneNumber` and `password`; the request interceptor adds `X-EduCenterOS-Auth: 1`, and the browser supplies the exact same Origin. A successful response contains a short-lived access JWT, tokenType and expiration; the refresh credential stays in the Secure HttpOnly SameSite Strict cookie. Keep access only in memory and do not enable Swagger authorization persistence. Registration still creates no session. Unknown/wrong/inactive/locked login returns the same safe 401; follow 429/Retry-After without blind credential replay. A failure around Commit must not be treated as proof that no session was created. `GET /api/v1/accounts/me` is active: use Swagger's Authorize with the in-memory access token to read only userAccountId, personIdentityId, fullName and createdAtUtc. Refresh cookie alone cannot authenticate it. Every protected request rechecks the account/session/security version in the database; revoked/expired sessions or inactive accounts fail on the next request. GET never extends session activity. `POST /api/v1/auth/refresh` is active with an empty JSON object and the browser's refresh cookie; optional access JWT is not authority. Use the same HTTPS Origin/CSRF marker. Each successful call consumes its credential, replaces the cookie and advances only LastSeen/idle, bounded by the original absolute expiration. Keep refresh single-flight: reusing any consumed ancestor commits session revocation and rejects every access/refresh descendant. If a real Commit succeeds but its response is lost, retrying the old cookie revokes the session and requires a new Login. There is no grace window or cached credential replay. Session-management routes follow in their owning S03 task.
