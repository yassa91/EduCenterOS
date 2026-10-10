#!/usr/bin/env python3
"""Explicit Supabase bootstrap. Secret command output is captured and never printed."""
import base64
import argparse
import json
import os
from pathlib import Path
import re
import secrets
import subprocess
import sys
import tempfile
from urllib.parse import urlsplit

ROOT = Path(__file__).resolve().parents[1]
TRUST = Path.home() / ".config/EduCenterOS/infisical-trust.json"
DATABASE_TRUST = Path.home() / ".config/EduCenterOS/supabase-targets.json"
MANIFEST = json.loads((ROOT / "infra/runtime.json").read_text())
PATHS = {"Development": "/backend-api/supabase-development", "Testing": "/backend-api/supabase-testing"}


def clean_environment():
    return {key: os.environ[key] for key in ("PATH", "HOME", "USER", "TMPDIR", "DOTNET_ROOT") if key in os.environ}


def command(arguments, *, input_text=None, environment=None, timeout=30):
    try:
        result = subprocess.run(arguments, input=input_text, stdin=None if input_text is not None else subprocess.DEVNULL,
                                capture_output=True, text=True, env=environment or clean_environment(), timeout=timeout, cwd=ROOT)
    except (OSError, subprocess.TimeoutExpired):
        raise RuntimeError("ToolUnavailableOrTimeout: " + arguments[0]) from None
    if result.returncode:
        raise RuntimeError("CommandFailed: " + arguments[0] + " (captured output withheld)")
    return result.stdout


def trusted_settings():
    if not TRUST.is_file() or TRUST.is_symlink():
        raise RuntimeError("TrustedInfisicalLocatorMissing")
    data = json.loads(TRUST.read_text())
    if set(data) != {"endpoint", "projectId", "environment", "path"} or data["environment"] != "dev" or data["path"] != "/backend-api/shared":
        raise RuntimeError("InvalidTrustedLocator")
    target = urlsplit(data["endpoint"])
    if target.scheme != "https" or not target.hostname or target.username or target.password or target.query or target.fragment or target.path != "/api":
        raise RuntimeError("InvalidTrustedEndpoint")
    if not re.fullmatch(r"[0-9a-f-]{36}", data["projectId"]):
        raise RuntimeError("InvalidProjectId")
    if command(["infisical", "--version"]).strip() != "infisical version " + MANIFEST["infisicalCliVersion"]:
        raise RuntimeError("InfisicalVersionMismatch")
    return data


def cli(arguments):
    data = trusted_settings()
    endpoint = data["endpoint"][:-4] if arguments[:2] == ["secrets", "folders"] else data["endpoint"]
    return command(["infisical", *arguments, "--domain=" + endpoint, "--projectId=" + data["projectId"],
                    "--env=dev", "--silent", "--telemetry=false"])


def reject_duplicate_pairs(pairs):
    result = {}
    seen = set()
    for key, value in pairs:
        if key.casefold() in seen:
            raise RuntimeError("DuplicateBootstrapKey")
        seen.add(key.casefold())
        result[key] = value
    return result


def decode_export(text):
    raw = json.loads(text, object_pairs_hook=reject_duplicate_pairs)
    pairs = list(raw.items()) if isinstance(raw, dict) else [
        (item.get("key", item.get("secretKey")), item.get("value", item.get("secretValue"))) for item in raw]
    result = {}
    seen = set()
    for key, value in pairs:
        if not isinstance(key, str) or not isinstance(value, str) or key.casefold() in seen:
            raise RuntimeError("InvalidSecretExport: missing fields or duplicate keys")
        seen.add(key.casefold())
        result[key] = value
    return result


def export(path):
    if path not in PATHS.values():
        raise RuntimeError("InvalidSecretPath")
    return decode_export(cli(["export", "--path=" + path, "--format=json", "--expand=false",
                              "--include-imports=false", "--secret-overriding=false"]))


def validate_target(target, environment):
    if set(target) != {"projectReference", "host", "environment", "serverMajor", "rootCertificate"}:
        raise RuntimeError("InvalidSupabaseLocator")
    ref, host = target["projectReference"], target["host"]
    if (not re.fullmatch(r"[a-z0-9]{20}", ref) or target["environment"] != environment
        or target["serverMajor"] not in (17, 18)
        or not (host == f"db.{ref}.supabase.co" or re.fullmatch(r"aws-[0-9]+-[a-z0-9-]+\.pooler\.supabase\.com", host))):
        raise RuntimeError("InvalidSupabaseLocator")
    cert = target["rootCertificate"]
    if cert and (not Path(cert).is_absolute() or not Path(cert).is_file() or Path(cert).is_symlink() or any(c in cert for c in ';\n\r"')):
        raise RuntimeError("InvalidCertificateLocator")
    return target


def database_target(environment):
    if DATABASE_TRUST.is_symlink() or not DATABASE_TRUST.is_file():
        raise RuntimeError("TrustedSupabaseLocatorMissing: run trust-database with reviewed project endpoints")
    targets = json.loads(DATABASE_TRUST.read_text(), object_pairs_hook=reject_duplicate_pairs)
    if set(targets) != {"Development", "Testing"}:
        raise RuntimeError("BothCloudTargetsRequired")
    for name, target in targets.items():
        validate_target(target, name)
    if targets["Development"]["projectReference"] == targets["Testing"]["projectReference"]:
        raise RuntimeError("DevelopmentAndTestingMustUseDistinctProjects")
    return targets[environment]


def public_target(target):
    return {key: value for key, value in target.items() if key != "rootCertificate"}


def role(target, purpose):
    return "educenteros_" + ("dev" if target["environment"] == "Development" else "test") + "_" + purpose


def username(target, purpose):
    name = "postgres" if purpose == "admin" else role(target, purpose)
    return name if target["host"].startswith("db.") else name + "." + target["projectReference"]


def connection_string(target, password, purpose):
    # Generated credentials are URL-safe. The admin password only reaches libpq's environment.
    if not re.fullmatch(r"[A-Za-z0-9_-]{32,128}", password):
        raise RuntimeError("InvalidGeneratedCredential")
    cert = ";Root Certificate=" + target["rootCertificate"] if target["rootCertificate"] else ""
    return (f"Host={target['host']};Port=5432;Database=postgres;Username={username(target,purpose)};Password={password}"
            + ";SSL Mode=VerifyFull;Timeout=10;Command Timeout=10;Maximum Pool Size=10;Include Error Detail=false" + cert)


def psql(target, password, purpose, sql):
    env = clean_environment()
    env.update(PGHOST=target["host"], PGPORT="5432", PGDATABASE="postgres", PGUSER=username(target, purpose),
               PGPASSWORD=password, PGSSLMODE="verify-full", PGCONNECT_TIMEOUT="10", PGAPPNAME="EduCenterOS-bootstrap")
    env["PGSSLROOTCERT"] = target["rootCertificate"] or "system"
    return command(["psql", "-X", "-qAt", "-v", "ON_ERROR_STOP=1"], input_text=sql, environment=env, timeout=60)


def bundle(environment, authentication_required=True):
    target = database_target(environment)
    values = export(PATHS[environment])
    required = {"SUPABASE_DATABASE_PASSWORD", "PROBE_PASSWORD", "RUNTIME_PASSWORD", "ConnectionStrings__RuntimeProbeDatabase", "ConnectionStrings__IdentityAccessDatabase"}
    required |= ({"MIGRATION_PASSWORD", "ConnectionStrings__IdentityAccessMigrationDatabase", "IdentityAccess__Otp__HashKeys__v1",
                  "IdentityAccess__Otp__CurrentHashKeyVersion", "Platform__RateLimiting__PartitionDigestKey"} if environment == "Development"
                 else {"OWNER_PASSWORD", "ConnectionStrings__TestOwnerDatabase"})
    if environment == "Development":
        auth = {key for key in values if key.startswith("IdentityAccess__Jwt__")}
        public = {key for key in auth if key.startswith("IdentityAccess__Jwt__ValidationPublicKeys__")}
        if auth or authentication_required:
            if not 1 <= len(public) <= 4:
                raise RuntimeError("AuthenticationBundleMissing: provision-auth is required")
            if auth != public | {"IdentityAccess__Jwt__PrivateKeyPem", "IdentityAccess__Jwt__CurrentKeyId"}:
                raise RuntimeError("InvalidAuthenticationBundle")
            current = values["IdentityAccess__Jwt__CurrentKeyId"]
            if (not re.fullmatch(r"[a-z][a-z0-9-]{0,63}", current) or
                "IdentityAccess__Jwt__ValidationPublicKeys__" + current not in public or
                any(not re.fullmatch(r"[a-z][a-z0-9-]{0,63}", key.split("__")[-1]) for key in public)):
                raise RuntimeError("InvalidAuthenticationBundle")
            required |= auth
    if set(values) != required:
        raise RuntimeError("IncompleteSupabaseBundle: provision-cloud is required")
    for purpose, key in [("probe", "RuntimeProbeDatabase"), ("runtime", "IdentityAccessDatabase"),
                         ("migration", "IdentityAccessMigrationDatabase") if environment == "Development" else ("owner", "TestOwnerDatabase")]:
        if values["ConnectionStrings__" + key] != connection_string(target, values[purpose.upper() + "_PASSWORD"], purpose):
            raise RuntimeError("SupabaseConnectionMismatch")
    return target, values


def provision(environment):
    target = database_target(environment)
    values = export(PATHS[environment])
    if set(values) == {"SUPABASE_DATABASE_PASSWORD"}:
        values.update(PROBE_PASSWORD=secrets.token_urlsafe(48), RUNTIME_PASSWORD=secrets.token_urlsafe(48))
        if environment == "Development":
            values.update(MIGRATION_PASSWORD=secrets.token_urlsafe(48), **{
                "IdentityAccess__Otp__HashKeys__v1": base64.b64encode(secrets.token_bytes(32)).decode(),
                "IdentityAccess__Otp__CurrentHashKeyVersion": "v1",
                "Platform__RateLimiting__PartitionDigestKey": base64.b64encode(secrets.token_bytes(32)).decode()})
        else:
            values["OWNER_PASSWORD"] = secrets.token_urlsafe(48)
        for purpose, key in [("probe", "RuntimeProbeDatabase"), ("runtime", "IdentityAccessDatabase"),
                             ("migration", "IdentityAccessMigrationDatabase") if environment == "Development" else ("owner", "TestOwnerDatabase")]:
            values["ConnectionStrings__" + key] = connection_string(target, values[purpose.upper() + "_PASSWORD"], purpose)
        with tempfile.TemporaryDirectory(prefix="educenteros-cloud-") as directory:
            file = Path(directory) / "bundle.env"
            descriptor = os.open(file, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
            with os.fdopen(descriptor, "w") as output:
                output.write("\n".join(key + "=" + json.dumps(value) for key, value in values.items()) + "\n")
            cli(["secrets", "set", "--path=" + PATHS[environment], "--type=shared", "--file=" + str(file)])
    if environment == "Development": provision_authentication()
    target, values = bundle(environment)
    actual = psql(target, values["SUPABASE_DATABASE_PASSWORD"], "admin",
                  "SELECT current_database() || ':' || current_user || ':' || (current_setting('server_version_num')::int/10000)::text;")
    if actual.strip() != f"postgres:postgres:{target['serverMajor']}":
        raise RuntimeError("CloudBootstrapIdentityMismatch")
    purposes = ["probe", "runtime", "migration" if environment == "Development" else "owner"]
    sql = "BEGIN;\n"
    for purpose in purposes:
        name = role(target, purpose)
        password = values[purpose.upper() + "_PASSWORD"]
        sql += (f"DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname='{name}') THEN "
                f"CREATE ROLE {name} LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '{password}'; END IF; END $$;\n"
                f"GRANT CONNECT ON DATABASE postgres TO {name};\n")
    owner = role(target, purposes[-1])
    # Membership permits postgres to provision objects owned by the restricted schema principal.
    sql += f"GRANT {owner} TO postgres;\nGRANT CREATE ON DATABASE postgres TO {owner};\nREVOKE CREATE ON SCHEMA public FROM PUBLIC;\n"
    if environment == "Development":
        sql += f"CREATE SCHEMA IF NOT EXISTS identity_access AUTHORIZATION {owner};\nREVOKE ALL ON SCHEMA identity_access FROM PUBLIC, anon, authenticated, service_role;\n"
    else:
        # This marker is deliberately outside fixture-owned cleanup schemas, writable only by postgres.
        sql += "CREATE SCHEMA IF NOT EXISTS educenteros_test_control AUTHORIZATION postgres;\n"
        sql += "REVOKE ALL ON SCHEMA educenteros_test_control FROM PUBLIC, anon, authenticated, service_role;\n"
        sql += "CREATE TABLE IF NOT EXISTS educenteros_test_control.target(project_reference text PRIMARY KEY, environment text NOT NULL);\n"
        sql += f"INSERT INTO educenteros_test_control.target VALUES ('{target['projectReference']}', 'Testing') ON CONFLICT DO NOTHING;\n"
        sql += f"GRANT USAGE ON SCHEMA educenteros_test_control TO {owner}; GRANT SELECT ON educenteros_test_control.target TO {owner};\n"
    sql += "COMMIT;\n"
    psql(target, values["SUPABASE_DATABASE_PASSWORD"], "admin", sql)
    print(environment + " cloud credentials and roles provisioned; no credentials displayed.")


def provision_authentication():
    # This explicit additive operation never overwrites existing DB/OTP/partition material.
    _, values = bundle("Development", authentication_required=False)
    if not any(key.startswith("IdentityAccess__Jwt__") for key in values):
        private = command(["openssl", "genpkey", "-algorithm", "RSA", "-pkeyopt", "rsa_keygen_bits:2048"])
        public = command(["openssl", "pkey", "-pubout"], input_text=private)
        added = {"IdentityAccess__Jwt__PrivateKeyPem": private.rstrip("\n"),
                 "IdentityAccess__Jwt__CurrentKeyId": "s03-dev-v1",
                 "IdentityAccess__Jwt__ValidationPublicKeys__s03-dev-v1": public.rstrip("\n")}
        with tempfile.TemporaryDirectory(prefix="educenteros-auth-") as directory:
            file = Path(directory) / "keys.yaml"
            descriptor = os.open(file, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
            with os.fdopen(descriptor, "w") as output:
                # JSON is a YAML subset; the YAML reader preserves PEM newline characters.
                json.dump(added, output)
                output.write("\n")
            cli(["secrets", "set", "--path=" + PATHS["Development"], "--type=shared", "--file=" + str(file)])
        _, updated = bundle("Development")
        if any(updated.get(key) != value for key, value in values.items()) or any(updated.get(key) != value for key, value in added.items()):
            raise RuntimeError("AuthenticationProvisionRoundTripMismatch")
        values = updated
    exported = command(["openssl", "pkey", "-pubout"], input_text=values["IdentityAccess__Jwt__PrivateKeyPem"])
    if exported.rstrip("\n") != values["IdentityAccess__Jwt__ValidationPublicKeys__" + values["IdentityAccess__Jwt__CurrentKeyId"]].rstrip("\n"):
        raise RuntimeError("AuthenticationSigningKeyMismatch")
    print("Development signing material validated in Infisical; existing credentials preserved.")


def migrate_identity():
    target, values = bundle("Development")
    environment = clean_environment()
    environment["EDUCENTEROS_MIGRATION_SNAPSHOT"] = json.dumps({"schemaVersion": 2, "environment": "Development", "source": "Infisical",
        "databaseTarget": public_target(target), "connectionString": values["ConnectionStrings__IdentityAccessMigrationDatabase"]})
    command(["dotnet", "run", "--project", str(ROOT / "tools/EduCenterOS.Migrator"), "-c", "Release", "--no-build"], environment=environment, timeout=180)
    runtime = role(target, "runtime")
    grants = (f"REVOKE ALL ON ALL TABLES IN SCHEMA identity_access FROM PUBLIC, anon, authenticated, service_role;\n"
              f"GRANT USAGE ON SCHEMA identity_access TO {runtime};\n"
              f"GRANT SELECT, INSERT, UPDATE, DELETE ON identity_access.person_identities, identity_access.user_accounts, identity_access.otp_challenges, "
              f"identity_access.verification_targets, identity_access.rate_key_binding, identity_access.user_sessions, "
              f"identity_access.refresh_token_records, identity_access.login_targets TO {runtime};\n"
              f"GRANT SELECT ON identity_access.__ef_migrations_history TO {runtime};\n")
    psql(target, values["MIGRATION_PASSWORD"], "migration", grants)
    print("Supabase migrations and restricted runtime grants completed.")


def test_snapshot(ci=False):
    target, values = bundle("Testing")
    connections = {"owner": values["ConnectionStrings__TestOwnerDatabase"],
        "probe": values["ConnectionStrings__RuntimeProbeDatabase"], "runtime": values["ConnectionStrings__IdentityAccessDatabase"]}
    if ci:
        if not target["rootCertificate"]: raise RuntimeError("ReviewedCloudCertificateRequiredForCI")
        for key, connection in connections.items():
            connections[key] = connection.replace(";Root Certificate=" + target["rootCertificate"], ";Root Certificate=__EDUCENTEROS_SUPABASE_CA__")
    return json.dumps({"databaseTarget": public_target(target), **connections})



def run():
    target, values = bundle("Development")
    runtime_secrets = {key: value for key, value in values.items() if key.startswith("IdentityAccess__")
        or key == "Platform__RateLimiting__PartitionDigestKey" or key in ("ConnectionStrings__RuntimeProbeDatabase", "ConnectionStrings__IdentityAccessDatabase")}
    snapshot = {"schemaVersion": 4, "environment": "Development", "source": "Infisical", "databaseTarget": public_target(target),
                "authenticationPolicy": json.loads((ROOT / "infra/authentication-policy.json").read_text()),
                "secrets": runtime_secrets, "securityPolicy": json.loads((ROOT / "infra/registration-policy.json").read_text()),
                "developmentMailboxDirectory": str(ROOT / ".local/otp")}
    environment = clean_environment()
    environment.update(DOTNET_ENVIRONMENT="Development", ASPNETCORE_ENVIRONMENT="Development", EDUCENTEROS_RUNTIME_SNAPSHOT=json.dumps(snapshot))
    result = subprocess.run(["dotnet", "run", "--project", str(ROOT / "src/EduCenterOS.Api"), "-c", "Release", "--no-launch-profile", "--no-build"], env=environment, cwd=ROOT)
    raise SystemExit(result.returncode)


def save_trusted_locator(path, data):
    with tempfile.NamedTemporaryFile(mode="w", dir=path.parent, delete=False) as output:
        json.dump(data, output, indent=2)
        output.write("\n")
        temporary = output.name
    os.chmod(temporary, 0o600)
    os.replace(temporary, path)


def trust_infisical(args, parser):
    if not args.endpoint or not args.project_id: parser.error("trust requires endpoint and project ID")
    if TRUST.is_symlink(): raise RuntimeError("InvalidTrustedLocator")
    data = {"endpoint": args.endpoint, "projectId": args.project_id, "environment": "dev", "path": "/backend-api/shared"}
    if TRUST.exists() and json.loads(TRUST.read_text()) != data: raise RuntimeError("ExistingTrustMismatch")
    TRUST.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
    save_trusted_locator(TRUST, data)
    trusted_settings()
    print("Infisical target trusted outside repository.")


def trust_database(args):
    target = validate_target({"environment": args.environment, "projectReference": args.project_reference,
        "host": args.host, "serverMajor": args.server_major, "rootCertificate": args.root_certificate}, args.environment)
    if DATABASE_TRUST.is_symlink(): raise RuntimeError("InvalidSupabaseLocator")
    DATABASE_TRUST.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
    targets = json.loads(DATABASE_TRUST.read_text()) if DATABASE_TRUST.exists() else {}
    if args.environment in targets and targets[args.environment] != target: raise RuntimeError("ExistingSupabaseTrustMismatch")
    if any(t["projectReference"] == target["projectReference"] and name != args.environment for name, t in targets.items()):
        raise RuntimeError("DevelopmentAndTestingMustUseDistinctProjects")
    targets[args.environment] = target
    save_trusted_locator(DATABASE_TRUST, targets)
    print("Reviewed cloud target trusted outside repository.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["trust", "trust-database", "provision-cloud", "provision-auth", "identity-migrate", "run"])
    parser.add_argument("--endpoint")
    parser.add_argument("--project-id")
    parser.add_argument("--environment", choices=PATHS, default="Development")
    parser.add_argument("--project-reference")
    parser.add_argument("--host")
    parser.add_argument("--server-major", type=int)
    parser.add_argument("--root-certificate", default="")
    args = parser.parse_args()
    if args.action == "trust":
        trust_infisical(args, parser)
    elif args.action == "trust-database":
        trust_database(args)
    elif args.action == "provision-cloud":
        provision(args.environment)
    elif args.action == "provision-auth":
        if args.environment != "Development":
            raise RuntimeError("AuthenticationProvisionIsDevelopmentOnly")
        provision_authentication()
    elif args.action == "identity-migrate":
        migrate_identity()
    else:
        run()


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, ValueError, KeyError, TypeError, OSError) as error:
        print(str(error) if isinstance(error, RuntimeError) else "InvalidBootstrapData", file=sys.stderr)
        raise SystemExit(1)
