#!/usr/bin/env python3
"""Explicit local bootstrap. Secret command output is captured and never printed."""
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
LOCAL = ROOT / ".local/dev-runtime"
MANIFEST = json.loads((ROOT / "infra/runtime.json").read_text())
IDENTITY_PATH = "/backend-api/identity-access"
IDENTITY_REQUIRED = {"POSTGRES_IDENTITY_RUNTIME_PASSWORD", "POSTGRES_IDENTITY_MIGRATION_PASSWORD",
                     "ConnectionStrings__IdentityAccessDatabase", "ConnectionStrings__IdentityAccessMigrationDatabase",
                     "IdentityAccess__Otp__HashKeys__v1", "IdentityAccess__Otp__CurrentHashKeyVersion", "Platform__RateLimiting__PartitionDigestKey"}
REQUIRED = {"POSTGRES_ADMIN_PASSWORD", "POSTGRES_RUNTIME_PASSWORD", "ConnectionStrings__RuntimeProbeDatabase"}


def clean_environment():
    return {key: os.environ[key] for key in ("PATH", "HOME", "USER", "TMPDIR", "DOTNET_ROOT") if key in os.environ}


def command(arguments, *, input_text=None, environment=None, timeout=30):
    try:
        result = subprocess.run(arguments, input=input_text, stdin=None if input_text is not None else subprocess.DEVNULL,
                                capture_output=True, text=True, env=environment or clean_environment(), timeout=timeout)
    except (OSError, subprocess.TimeoutExpired):
        raise RuntimeError("ToolUnavailableOrTimeout: " + arguments[0]) from None
    if result.returncode:
        raise RuntimeError("CommandFailed: " + arguments[0] + " (captured output withheld)")
    return result.stdout


def trusted_settings():
    if not TRUST.is_file() or TRUST.is_symlink():
        raise RuntimeError("TrustedLocatorMissing: run the explicit trust command first")
    data = json.loads(TRUST.read_text())
    if set(data) != {"endpoint", "projectId", "environment", "path"} or data["environment"] != "dev" or data["path"] != "/backend-api/shared":
        raise RuntimeError("InvalidTrustedLocator")
    target = urlsplit(data["endpoint"])
    if target.scheme != "https" or not target.hostname or target.username or target.password or target.query or target.fragment or target.path != "/api":
        raise RuntimeError("InvalidTrustedEndpoint")
    if not re.fullmatch(r"[0-9a-f-]{36}", data["projectId"]):
        raise RuntimeError("InvalidProjectId")
    version = command(["infisical", "--version"]).strip()
    if version != "infisical version " + MANIFEST["infisicalCliVersion"]:
        raise RuntimeError("InfisicalVersionMismatch: use the pinned CLI or review a version update")
    return data


def cli(arguments):
    data = trusted_settings()
    return command(["infisical", *arguments, "--domain=" + data["endpoint"], "--projectId=" + data["projectId"],
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


def export(path="/backend-api/shared"):
    if path not in ("/backend-api/shared", IDENTITY_PATH):
        raise RuntimeError("InvalidSecretPath")
    return decode_export(cli(["export", "--path=" + path, "--format=json", "--expand=false",
                              "--include-imports=false", "--secret-overriding=false"]))


def bundle():
    values = export()
    if set(values) != REQUIRED:
        raise RuntimeError("IncompleteSecretSnapshot: provision the exact active foundation keys")
    for key in ("POSTGRES_ADMIN_PASSWORD", "POSTGRES_RUNTIME_PASSWORD"):
        if not re.fullmatch(r"[A-Za-z0-9_-]{32,128}", values[key]):
            raise RuntimeError("InvalidDevelopmentCredentialFormat")
    if values["ConnectionStrings__RuntimeProbeDatabase"] != connection_string(values["POSTGRES_RUNTIME_PASSWORD"]):
        raise RuntimeError("DevelopmentConnectionMismatch")
    return values


def connection_string(password):
    return ("Host=127.0.0.1;Port=55432;Database=educenteros_dev;Username=educenteros_runtime_probe;Password="
            + password + ";Timeout=3;Command Timeout=3;Include Error Detail=false")


def provision():
    for parent, name in [("/", "backend-api"), ("/backend-api", "shared")]:
        raw = json.loads(cli(["secrets", "folders", "get", "--path=" + parent, "--output=json"]))
        folders = (raw.get("folders") or []) if isinstance(raw, dict) else (raw or [])
        if name not in [item.get("name", item.get("folderName")) for item in folders]:
            cli(["secrets", "folders", "create", "--path=" + parent, "--name=" + name, "--output=json"])
    existing = export()
    if existing:
        bundle()
        print("Development foundation secrets already provisioned; no values changed.")
        return
    values = {"POSTGRES_ADMIN_PASSWORD": secrets.token_urlsafe(48), "POSTGRES_RUNTIME_PASSWORD": secrets.token_urlsafe(48)}
    values["ConnectionStrings__RuntimeProbeDatabase"] = connection_string(values["POSTGRES_RUNTIME_PASSWORD"])
    with tempfile.TemporaryDirectory(prefix="educenteros-provision-") as directory:
        file = Path(directory) / "provision.env"
        file.write_text("\n".join(key + "=" + json.dumps(value) for key, value in values.items()) + "\n")
        file.chmod(0o600)
        cli(["secrets", "set", "--path=/backend-api/shared", "--type=shared", "--file=" + str(file)])
    bundle()
    print("Development foundation secrets provisioned in Infisical; values were not displayed.")


def identity_connection(password, migration=False):
    role = "educenteros_identity_migration" if migration else "educenteros_identity_runtime"
    return ("Host=127.0.0.1;Port=55432;Database=educenteros_dev;Username=" + role + ";Password="
            + password + ";Timeout=3;Command Timeout=5;Include Error Detail=false")


def identity_bundle():
    values = export(IDENTITY_PATH)
    if set(values) != IDENTITY_REQUIRED:
        raise RuntimeError("IncompleteIdentitySnapshot: run provision-identity before activation")
    for key in ("POSTGRES_IDENTITY_RUNTIME_PASSWORD", "POSTGRES_IDENTITY_MIGRATION_PASSWORD"):
        if not re.fullmatch(r"[A-Za-z0-9_-]{32,128}", values[key]):
            raise RuntimeError("InvalidIdentityCredentialFormat")
    for key in ("IdentityAccess__Otp__HashKeys__v1", "Platform__RateLimiting__PartitionDigestKey"):
        try:
            material = base64.b64decode(values[key], validate=True)
        except ValueError:
            raise RuntimeError("InvalidIdentityKeyFormat") from None
        if len(material) != 32 or base64.b64encode(material).decode() != values[key]:
            raise RuntimeError("InvalidIdentityKeyFormat")
    if values["IdentityAccess__Otp__CurrentHashKeyVersion"] != "v1" or values["IdentityAccess__Otp__HashKeys__v1"] == values["Platform__RateLimiting__PartitionDigestKey"]:
        raise RuntimeError("InvalidIdentityKeyInventory")
    if values["ConnectionStrings__IdentityAccessDatabase"] != identity_connection(values["POSTGRES_IDENTITY_RUNTIME_PASSWORD"]) or values["ConnectionStrings__IdentityAccessMigrationDatabase"] != identity_connection(values["POSTGRES_IDENTITY_MIGRATION_PASSWORD"], True):
        raise RuntimeError("IdentityConnectionMismatch")
    return values


def provision_identity():
    bundle()  # Preserve and validate the existing S01 foundation; never overwrite it.
    raw = json.loads(cli(["secrets", "folders", "get", "--path=/backend-api", "--output=json"]))
    folders = raw.get("folders", []) if isinstance(raw, dict) else raw
    if "identity-access" not in [item.get("name", item.get("folderName")) for item in folders]:
        cli(["secrets", "folders", "create", "--path=/backend-api", "--name=identity-access", "--output=json"])
    if export(IDENTITY_PATH):
        identity_bundle()
        print("IdentityAccess secrets already complete; no existing values changed.")
        return
    values = {"POSTGRES_IDENTITY_RUNTIME_PASSWORD": secrets.token_urlsafe(48),
              "POSTGRES_IDENTITY_MIGRATION_PASSWORD": secrets.token_urlsafe(48),
              "IdentityAccess__Otp__HashKeys__v1": base64.b64encode(secrets.token_bytes(32)).decode(),
              "IdentityAccess__Otp__CurrentHashKeyVersion": "v1",
              "Platform__RateLimiting__PartitionDigestKey": base64.b64encode(secrets.token_bytes(32)).decode()}
    values["ConnectionStrings__IdentityAccessDatabase"] = identity_connection(values["POSTGRES_IDENTITY_RUNTIME_PASSWORD"])
    values["ConnectionStrings__IdentityAccessMigrationDatabase"] = identity_connection(values["POSTGRES_IDENTITY_MIGRATION_PASSWORD"], True)
    with tempfile.TemporaryDirectory(prefix="educenteros-identity-") as directory:
        file = Path(directory) / "identity.env"
        descriptor = os.open(file, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(descriptor, "w") as handle:
            handle.write("\n".join(key + "=" + json.dumps(value) for key, value in values.items()) + "\n")
        cli(["secrets", "set", "--path=" + IDENTITY_PATH, "--type=shared", "--file=" + str(file)])
    identity_bundle()
    print("IdentityAccess secrets provisioned separately; no secret values displayed.")


def migrate_identity():
    bundle()
    values = identity_bundle()
    identity = compose(["exec", "-T", "postgres", "psql", "-U", "educenteros_admin", "-d", "educenteros_dev", "-At", "-c",
                        "SELECT current_database() || ':' || current_user || ':' || (current_setting('server_version_num')::integer / 10000)::text"])
    if identity.strip() != "educenteros_dev:educenteros_admin:18":
        raise RuntimeError("MigrationBootstrapTargetMismatch")
    runtime_password = values["POSTGRES_IDENTITY_RUNTIME_PASSWORD"]
    migration_password = values["POSTGRES_IDENTITY_MIGRATION_PASSWORD"]
    sql = ("DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname='educenteros_identity_migration') THEN "
           "CREATE ROLE educenteros_identity_migration LOGIN PASSWORD '" + migration_password + "'; END IF; "
           "IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname='educenteros_identity_runtime') THEN "
           "CREATE ROLE educenteros_identity_runtime LOGIN PASSWORD '" + runtime_password + "'; END IF; END $$;\n"
           "CREATE SCHEMA IF NOT EXISTS identity_access AUTHORIZATION educenteros_identity_migration;\n"
           "GRANT CONNECT ON DATABASE educenteros_dev TO educenteros_identity_migration, educenteros_identity_runtime;\n")
    compose(["exec", "-T", "postgres", "psql", "-U", "educenteros_admin", "-d", "educenteros_dev", "-v", "ON_ERROR_STOP=1"], input_text=sql)
    environment = clean_environment()
    environment["EDUCENTEROS_MIGRATION_SNAPSHOT"] = json.dumps({"schemaVersion": 1, "environment": "Development", "source": "Infisical",
                                                              "connectionString": values["ConnectionStrings__IdentityAccessMigrationDatabase"]})
    command(["dotnet", "run", "--project", str(ROOT / "tools/EduCenterOS.Migrator"), "-c", "Release", "--no-build"], environment=environment, timeout=60)
    grants = ("GRANT USAGE ON SCHEMA identity_access TO educenteros_identity_runtime;\n"
              "GRANT SELECT, INSERT, UPDATE, DELETE ON identity_access.person_identities, identity_access.user_accounts, "
              "identity_access.otp_challenges, identity_access.verification_targets, identity_access.rate_key_binding TO educenteros_identity_runtime;\n"
              "GRANT SELECT ON identity_access.__ef_migrations_history TO educenteros_identity_runtime;\n")
    compose(["exec", "-T", "postgres", "psql", "-U", "educenteros_admin", "-d", "educenteros_dev", "-v", "ON_ERROR_STOP=1"], input_text=grants)
    print("IdentityAccess migrations and restricted runtime grants completed; credentials stayed outside API/arguments.")


def compose(arguments, *, input_text=None):
    environment = clean_environment()
    environment["EDUCENTEROS_POSTGRES_IMAGE"] = MANIFEST["postgresImage"]
    environment["EDUCENTEROS_POSTGRES_ADMIN_SECRET_FILE"] = str(LOCAL / "admin-password")
    return command(["docker", "compose", "-f", str(ROOT / "infra/compose.yml"), *arguments],
                   environment=environment, input_text=input_text, timeout=90)


def database_start():
    values = bundle()
    if LOCAL.is_symlink():
        raise RuntimeError("InvalidRuntimeMount")
    LOCAL.mkdir(parents=True, exist_ok=True, mode=0o700)
    LOCAL.chmod(0o700)
    password_file = LOCAL / "admin-password"
    if password_file.exists():
        if password_file.is_symlink() or password_file.read_text() != values["POSTGRES_ADMIN_PASSWORD"]:
            raise RuntimeError("RuntimeMountMismatch: stop the owned dev container before reviewed rotation")
    else:
        with password_file.open("x") as handle:
            handle.write(values["POSTGRES_ADMIN_PASSWORD"])
        password_file.chmod(0o600)
    compose(["up", "-d", "--wait", "--wait-timeout", "60"])
    password = values["POSTGRES_RUNTIME_PASSWORD"]
    sql = ("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'educenteros_runtime_probe') THEN "
           "CREATE ROLE educenteros_runtime_probe LOGIN PASSWORD '" + password + "'; END IF; END $$;\n"
           "GRANT CONNECT ON DATABASE educenteros_dev TO educenteros_runtime_probe;\n")
    compose(["exec", "-T", "postgres", "psql", "-U", "educenteros_admin", "-d", "educenteros_dev", "-v", "ON_ERROR_STOP=1"], input_text=sql)
    print("PostgreSQL development service is ready; volume retained and runtime role provisioned.")


def database_stop():
    if not (LOCAL / "admin-password").exists():
        raise RuntimeError("RuntimeMountMissing: stop requires the owned runtime locator")
    compose(["down"])
    (LOCAL / "admin-password").unlink()
    LOCAL.rmdir()
    print("Development containers stopped; temporary secret mount removed and data volume retained.")


def run():
    values = bundle()
    identity = identity_bundle()
    runtime_secrets = {"ConnectionStrings__RuntimeProbeDatabase": values["ConnectionStrings__RuntimeProbeDatabase"]}
    runtime_secrets.update({key: value for key, value in identity.items() if key.startswith("IdentityAccess__") or key == "Platform__RateLimiting__PartitionDigestKey" or key == "ConnectionStrings__IdentityAccessDatabase"})
    snapshot = {"schemaVersion": 2, "environment": "Development", "source": "Infisical", "secrets": runtime_secrets,
                "securityPolicy": json.loads((ROOT / "infra/registration-policy.json").read_text()), "developmentMailboxDirectory": str(ROOT / ".local/otp")}
    environment = clean_environment()
    environment.update({"DOTNET_ENVIRONMENT": "Development", "ASPNETCORE_ENVIRONMENT": "Development",
                        "EDUCENTEROS_RUNTIME_SNAPSHOT": json.dumps(snapshot)})
    result = subprocess.run(["dotnet", "run", "--project", str(ROOT / "src/EduCenterOS.Api"), "-c", "Release",
                             "--no-launch-profile", "--no-build"], env=environment)
    raise SystemExit(result.returncode)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["trust", "provision", "database-start", "database-stop", "provision-identity", "identity-migrate", "run"])
    parser.add_argument("--endpoint")
    parser.add_argument("--project-id")
    args = parser.parse_args()
    if args.action == "trust":
        if not args.endpoint or not args.project_id:
            parser.error("trust requires explicit --endpoint and --project-id")
        if TRUST.is_symlink():
            raise RuntimeError("InvalidTrustedLocator")
        TRUST.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
        TRUST.parent.chmod(0o700)
        data = {"endpoint": args.endpoint, "projectId": args.project_id, "environment": "dev", "path": "/backend-api/shared"}
        if TRUST.exists() and json.loads(TRUST.read_text()) != data:
            raise RuntimeError("ExistingTrustMismatch: review the existing trusted target first")
        TRUST.write_text(json.dumps(data, indent=2) + "\n")
        TRUST.chmod(0o600)
        trusted_settings()
        print("Explicit development target trusted outside the repository.")
    else:
        {"provision": provision, "database-start": database_start, "database-stop": database_stop, "provision-identity": provision_identity, "identity-migrate": migrate_identity, "run": run}[args.action]()


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, ValueError, KeyError, TypeError) as error:
        # Error codes contain no captured command output, config dump or secret value.
        message = str(error) if isinstance(error, RuntimeError) else "InvalidBootstrapData"
        print(message, file=sys.stderr)
        raise SystemExit(1)
