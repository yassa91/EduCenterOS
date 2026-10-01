# EduCenterOS

Backend foundation for education center management. Sprint S01 provides the local API host, safe runtime configuration, PostgreSQL, isolated tests and CI. S02 is complete: IdentityAccess persistence, security configuration and local OTP delivery are active; the full request/resend/verify/register account flow is active. Frontend and production deployment remain outside this sprint.

## Prerequisites

- .NET SDK from `global.json` (10.0.203, same feature-band patch roll-forward).
- Python 3.11+ and Git.
- Local Docker Engine/Desktop with Linux containers running. Verification provisions PostgreSQL 18 containers on loopback ports; it never resets a developer database.
- For real development only: Infisical CLI 0.43.120 and access to the project's `dev` environment. Automated tests do not require Infisical or real runtime secrets.

Run commands from the repository root. Native startup and helper scripts support macOS/Linux; Windows can use a Linux development environment.

## Build and verify a fresh clone

```sh
git clone https://github.com/yassa91/EduCenterOS.git
cd EduCenterOS
python3 scripts/verify.py
```

The gate performs locked restore, Release build, pinned Docker image acquisition, then Unit, Integration and Architecture suites. It selects `Testing` explicitly, removes inherited runtime-secret configuration, and fails for zero discovery, failed/skipped tests or missing infrastructure. Unit and Architecture can run without Docker after restore/build:

```sh
dotnet restore EduCenterOS.sln --locked-mode
dotnet build EduCenterOS.sln -c Release --no-restore
dotnet test tests/EduCenterOS.UnitTests/EduCenterOS.UnitTests.csproj -c Release --no-build
dotnet test tests/EduCenterOS.ArchitectureTests/EduCenterOS.ArchitectureTests.csproj -c Release --no-build
```

For a direct Integration run, set both environment selectors to `Testing` in that process:

```sh
DOTNET_ENVIRONMENT=Testing ASPNETCORE_ENVIRONMENT=Testing dotnet test tests/EduCenterOS.IntegrationTests/EduCenterOS.IntegrationTests.csproj -c Release --no-build
```

TestServer verifies the in-process HTTP pipeline. The disposable database uses actual PostgreSQL 18 with random test-only credentials, a restricted runtime role and an ownership guard. It verifies fixture identity/lease before IdentityAccess migrations or reset. EF history is owned by the module; runtime tests use a restricted schema role.

## Real development startup

First authenticate on this machine; do not paste passwords, tokens or connection strings into Git or chat:

```sh
infisical login --domain=https://app.infisical.com/api
python3 scripts/dev.py trust --endpoint https://app.infisical.com/api --project-id 4243f1e5-ad83-4044-9160-cf1819e70d90
python3 scripts/dev.py provision
python3 scripts/dev.py provision-identity
python3 scripts/dev.py database-start
dotnet restore EduCenterOS.sln --locked-mode
dotnet build EduCenterOS.sln -c Release --no-restore
python3 scripts/dev.py identity-migrate
python3 scripts/dev.py run
```

Choose the organization containing EduCenterOS during login. `trust` stores the non-secret locator outside Git at `~/.config/EduCenterOS/infisical-trust.json`. The launcher deliberately ignores repository `.infisical.json` defaults. `provision` creates `/backend-api/shared` and its three dev secrets only when empty; a complete existing bundle is validated, while partial or unexpected data fail without overwriting it. IdentityAccess gets a separate exact bundle at `/backend-api/identity-access`; `provision-identity` creates it only when empty and never changes S01 values. A fresh workstation with access to the existing project can use the same commands. `identity-migrate` validates the actual PostgreSQL 18 dev target and schema owner, uses a dedicated migration role in a separate process, then grants runtime table access. Migration credentials never reach the API and startup never runs migrations.

Docker binds PostgreSQL to `127.0.0.1:55432`. The native API binds to `127.0.0.1:5100`. The API receives a complete environment-bound snapshot freshly captured from Infisical; it has no implicit environment/user-secret/command-line connection-string fallback.

In another terminal:

```sh
curl --noproxy '*' -i http://127.0.0.1:5100/health/live
curl --noproxy '*' -i http://127.0.0.1:5100/health/ready
```

Both support GET/HEAD. Liveness checks the host; readiness checks both named database connections with bounded probes. Responses contain `Healthy` (200) or `Unhealthy` (503), a server-generated correlation ID and `Cache-Control: no-store`. Unmatched routes and unsupported methods use safe ProblemDetails. Health routes are excluded from the public API description; the current business OpenAPI document is available at `/openapi/v1.json`; it describes only implemented routes.

Stop the API with Ctrl+C, then:

```sh
python3 scripts/dev.py database-stop
```

This stops the development container and removes the temporary password mount while preserving the named data volume. Avoid manual volume deletion unless you intentionally want to erase development data.

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
- If dev bootstrap fails, check CLI version/login/project access and Docker availability. Scripts hide raw provider output and never print secret values. Do not bypass validation by copying a dev connection string into appsettings.
- Each verification run has a unique ignored directory under `artifacts/test-results/`. Raw local logs/TRX are private diagnostics and may contain failed-test input; review them before sharing. CI uploads only an allowlisted JSON projection (static test names, outcomes and counts), without stdout, attachments, paths or failure text, for seven days.
- CI `verify` runs on every PR into `main`, pushes to `main` and manual dispatch. A failure remains a failure; there are no automatic retries or required skips.
- Each task uses a new branch from updated `main`, review and Squash Merge according to [T40](docs/technical/T40%20-%20Git%20%26%20GitHub%20Workflow.md). Automatic merge is delegated after acceptance, checks and review. The current private-repository plan blocks GitHub branch protection; these gates are enforced procedurally until server-side protection is available.

The approved registration scope and current delivery evidence are in [S02](docs/sprints/S02.md) and [the registration contract](docs/contracts/S02-registration.md). Foundation evidence is in [S01](docs/sprints/S01.md). Runtime, tests and CI contracts are in [T37](docs/technical/T37%20-%20Local%20Runtime%20%26%20Docker.md), [T33](docs/technical/T33%20-%20Testing%20Stack.md) and [T38](docs/technical/T38%20-%20CI%20%26%20Verification.md).
