# EduCenterOS

Backend foundation for education center management. Sprint S01 provides the local API host, safe runtime configuration, PostgreSQL, isolated tests and CI. Business modules, accounts, frontend and production deployment follow in later approved sprints.

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

TestServer verifies the in-process HTTP pipeline. The disposable database uses actual PostgreSQL 18 with random test-only credentials, a restricted runtime role and an ownership guard. It creates technical test metadata only; no business migrations exist yet.

## Real development startup

First authenticate on this machine; do not paste passwords, tokens or connection strings into Git or chat:

```sh
infisical login --domain=https://app.infisical.com/api
python3 scripts/dev.py trust --endpoint https://app.infisical.com/api --project-id 4243f1e5-ad83-4044-9160-cf1819e70d90
python3 scripts/dev.py provision
python3 scripts/dev.py database-start
dotnet restore EduCenterOS.sln --locked-mode
dotnet build EduCenterOS.sln -c Release --no-restore
python3 scripts/dev.py run
```

Choose the organization containing EduCenterOS during login. `trust` stores the non-secret locator outside Git at `~/.config/EduCenterOS/infisical-trust.json`. The launcher deliberately ignores repository `.infisical.json` defaults. `provision` creates `/backend-api/shared` and its three dev secrets only when empty; a complete existing bundle is validated, while partial or unexpected data fail without overwriting it. A fresh workstation with access to the existing project can use the same commands.

Docker binds PostgreSQL to `127.0.0.1:55432`. The native API binds to `127.0.0.1:5100`. The API receives a complete environment-bound snapshot freshly captured from Infisical; it has no implicit environment/user-secret/command-line connection-string fallback.

In another terminal:

```sh
curl --noproxy '*' -i http://127.0.0.1:5100/health/live
curl --noproxy '*' -i http://127.0.0.1:5100/health/ready
```

Both support GET/HEAD. Liveness checks the host; readiness checks PostgreSQL with a bounded probe. Responses contain `Healthy` (200) or `Unhealthy` (503), a server-generated correlation ID and `Cache-Control: no-store`. Unmatched routes and unsupported methods use safe ProblemDetails. Health routes are excluded from the public API description; no business OpenAPI surface exists yet.

Stop the API with Ctrl+C, then:

```sh
python3 scripts/dev.py database-stop
```

This stops the development container and removes the temporary password mount while preserving the named data volume. Avoid manual volume deletion unless you intentionally want to erase development data.

## Diagnostics and delivery

- Missing/conflicting environment, invalid startup configuration or missing snapshot fail before readiness. Staging/Production require T39 and are currently disabled.
- If dev bootstrap fails, check CLI version/login/project access and Docker availability. Scripts hide raw provider output and never print secret values. Do not bypass validation by copying a dev connection string into appsettings.
- Each verification run has a unique ignored directory under `artifacts/test-results/`. Raw local logs/TRX are private diagnostics and may contain failed-test input; review them before sharing. CI uploads only an allowlisted JSON projection (static test names, outcomes and counts), without stdout, attachments, paths or failure text, for seven days.
- CI `verify` runs on every PR into `main`, pushes to `main` and manual dispatch. A failure remains a failure; there are no automatic retries or required skips.
- Each task uses a new branch from updated `main`, review and Squash Merge according to [T40](docs/technical/T40%20-%20Git%20%26%20GitHub%20Workflow.md). Automatic merge is delegated after acceptance, checks and review. The current private-repository plan blocks GitHub branch protection; these gates are enforced procedurally until server-side protection is available.

The approved scope, acceptance criteria and delivery evidence are in [S01](docs/sprints/S01.md). Runtime, tests and CI contracts are in [T37](docs/technical/T37%20-%20Local%20Runtime%20%26%20Docker.md), [T33](docs/technical/T33%20-%20Testing%20Stack.md) and [T38](docs/technical/T38%20-%20CI%20%26%20Verification.md).
