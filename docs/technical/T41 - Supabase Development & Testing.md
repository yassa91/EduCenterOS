# T41 — Supabase Development & Testing

Owner-authorized on 2026-10-03. DB01 implementation/remote acceptance in progress.
This replaces T37's local PostgreSQL, Docker and Testcontainers provisioning contracts for active development and tests.
Historical S01/S02 acceptance describes the system at those dates.

## Targets and database version

| Purpose | Supabase project | PostgreSQL |
| --- | --- | --- |
| Development | `hwuqjxbcbpbsdbonpdpf` — EduCenterOS-dev | 17 (dashboard 17.11.0.002) |
| Testing | `fvrvmmkxtifxslwunlko` — EduCenterOS-test | 17 (actual provisioning verified) |

Start Development empty and apply the existing three EF migrations. No local-data migration.
EF Core 10 and Npgsql 10 remain the persistence stack. The previous PostgreSQL 18-only
baseline is replaced by managed PostgreSQL 17/18, pinned per trusted target; actual server
major is verified before migrations/test cleanup. Compatibility is an acceptance gate, not an assumption.
Future upgrades still require review and full migration/persistence/transaction/concurrency tests.

## Connection and secrets

Use direct 5432 when IPv6 is reachable, otherwise dashboard-provided shared Session Pooler 5432.
Do not infer a pooler host from region; do not use transaction pooler 6543.
Npgsql SSL Mode VerifyFull and libpq verify-full check certificate trust and hostname.
Both current shared poolers require the official Supabase CA. When an endpoint needs this CA,
install its public certificate outside Git and pass its absolute path. Never disable validation.
Small connection pools (maximum 10) bound sessions; no auto-prepare or multiplexing.

`~/.config/EduCenterOS/supabase-targets.json` records both project references, endpoints,
environments, server majors and optional CA paths independently of repository configuration.
Both targets are required and references must differ. Old local connections fail closed.
The official public (non-secret) CA is also reviewed at `infra/supabase-ca.crt`, SHA256
`700723581420dd1ac98fd7e9ac529f0ef210eadcaf87fc868a3ad7d114c2f3b7`, downloaded from the dashboard's certificate link.
CI's derived snapshot uses `__EDUCENTEROS_SUPABASE_CA__`; verification binds that marker to this checkout's public CA path.
Certificate rotation requires provider verification/review and a connection check.
Testing snapshot provenance is `CloudTestFixture`; fixture security keys/data remain synthetic but infrastructure credentials are real.
The runtime snapshot is schema 3 and includes the non-secret target. Migration snapshot is schema 2.

Infisical remains the source of real credentials. New scopes, both under the existing Infisical `dev` environment:

- `/backend-api/supabase-development`: bootstrap database password, restricted probe/runtime/migration
  passwords and connections, OTP keyring/current version and partition key.
- `/backend-api/supabase-testing`: bootstrap database password, restricted probe/runtime/owner passwords and connections.

Each scope initially contains only `SUPABASE_DATABASE_PASSWORD`, entered privately by the owner.
`provision-cloud` adds a complete bundle once. Existing complete bundles are validated; partial,
unknown and duplicate keys fail. Password changes require explicit database/consumer rotation.
Old `/backend-api/shared` and `/backend-api/identity-access` scopes remain inactive, preserving rollback material.
No passwords in arguments/logs, no repository credential files, no admin/migration credentials in the API.

CI receives a derived, test-only snapshot as GitHub Actions secret `EDUCENTEROS_TEST_DATABASE_SNAPSHOT`.
Infisical is authoritative; refresh the derived CI secret after reviewed test-credential rotation.
This deliberately scoped CI distribution replaces the old synthetic-only test-credential rule
because the owner requested cloud tests. No development/admin credentials or OTP keys enter CI.
Unit/Architecture and build/restore receive no database snapshot. Missing cloud access fails the gate without skip.

## Roles, schemas and exposure

Development roles: `educenteros_dev_probe`, `educenteros_dev_runtime`, `educenteros_dev_migration`.
Testing roles: `educenteros_test_probe`, `educenteros_test_runtime`, `educenteros_test_owner`.
All are NOINHERIT, NOSUPERUSER, NOCREATEDB, NOCREATEROLE, NOREPLICATION, NOBYPASSRLS.
The schema principal has database CREATE for schemas; application runtime has only module CRUD,
USAGE on `identity_access` and read-only EF history. Probe only connects and executes SELECT 1.
Shared pooler usernames append `.<project-reference>`; PostgreSQL current_user remains the plain role.
Supabase postgres is used by explicit provisioning only.

Keep Data API disabled for these database-only projects. Do not add internal schemas to exposed schemas.
Revoke PUBLIC/anon/authenticated/service_role schema/table privileges; no browser receives PostgreSQL credentials.
Supabase Auth is not introduced: IdentityAccess account/OTP rules stay in the backend.

## Cloud test ownership and cleanup

The test owner can read an immutable `educenteros_test_control.target` marker owned by postgres.
It cannot write that marker. The marker records the trusted test-project reference and Testing environment.
Before any cleanup, verify connection endpoint/TLS/role/database, actual PostgreSQL major,
non-admin role, the project marker and a live session-scoped advisory lock. Only one fixture/run
uses the test project at a time; concurrent attempts fail before mutation.
A random `test_support.run_ownership` lease adds per-fixture ownership checks.
The only reset/drop schemas are `identity_access` and `test_support`; never database/server drop,
Supabase-managed schemas, public, wildcards or development cleanup.
Cleanup happens before releasing the exclusive lease. A crash leaves owned schema data for the next
verified lease to replace; loss of ownership prevents cleanup rather than guessing.

Integration test data and OTP/partition keys are synthetic; test infrastructure credentials are real,
test-project-only secrets. Existing migration-upgrade test resets the owned migration schema
inside the current lease instead of nesting another fixture. Outage test injects a refused TCP
endpoint into its own readiness probe and restores it; it does not pause a cloud server.

## Local retirement

Active code has no Compose startup, PostgreSQL container, Testcontainers or Docker requirement.
After remote migrations, health, registration flow, all suites and CI pass, stop only the owned
`educenteros-dev` local service. Preserve the old volume as rollback until the owner requests its deletion.
The API and protected Development OTP mailbox continue running locally; database storage is cloud.
The separate uncommitted macOS launcher must also be updated before using it for cloud startup.

## Primary references

- [Supabase connections](https://supabase.com/docs/guides/database/connecting-to-postgres)
- [Supabase roles](https://supabase.com/docs/guides/database/postgres/roles)
- [Supabase SSL](https://supabase.com/docs/guides/platform/ssl-enforcement)
