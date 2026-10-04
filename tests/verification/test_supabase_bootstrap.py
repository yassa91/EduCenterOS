import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("supabase_dev", ROOT / "scripts/dev.py")
dev = importlib.util.module_from_spec(spec)
spec.loader.exec_module(dev)


class SupabaseBootstrapTests(unittest.TestCase):
    def target(self, environment="Development"):
        return {"projectReference": "abcdefghijklmnopqrst", "host": "db.abcdefghijklmnopqrst.supabase.co",
                "environment": environment, "serverMajor": 17, "rootCertificate": ""}

    def test_target_rejects_local_transaction_and_unreviewed_hosts(self):
        for host in ("127.0.0.1", "aws-0-eu-west-1.pooler.supabase.com:6543", "evil.supabase.com", "db.zyxwvutsrqponmlkjihg.supabase.co"):
            with self.subTest(host=host):
                target = self.target(); target["host"] = host
                with self.assertRaises(RuntimeError): dev.validate_target(target, "Development")

    def test_two_projects_are_required_and_cannot_match(self):
        import json
        with tempfile.TemporaryDirectory() as directory:
            file = Path(directory) / "trust.json"
            with patch.object(dev, "DATABASE_TRUST", file):
                file.write_text(json.dumps({"Development": self.target()}))
                with self.assertRaisesRegex(RuntimeError, "BothCloudTargetsRequired"): dev.database_target("Development")
                file.write_text(json.dumps({"Development": self.target(), "Testing": self.target("Testing")}))
                with self.assertRaisesRegex(RuntimeError, "DistinctProjects"): dev.database_target("Testing")

    def test_psql_credentials_use_environment_and_verified_tls(self):
        with patch.object(dev, "command", return_value="") as command:
            dev.psql(self.target(), "sensitive-marker", "admin", "SELECT 1")
            args, kwargs = command.call_args
            self.assertNotIn("sensitive-marker", repr(args))
            self.assertEqual("sensitive-marker", kwargs["environment"]["PGPASSWORD"])
            self.assertEqual("verify-full", kwargs["environment"]["PGSSLMODE"])
            self.assertIn("-X", args[0])

    def test_generated_connection_uses_environment_specific_role(self):
        connection = dev.connection_string(self.target("Testing"), "x" * 32, "runtime")
        self.assertIn("educenteros_test_runtime", connection)
        self.assertIn("SSL Mode=VerifyFull", connection)
        self.assertNotIn("127.0.0.1", connection)

    def test_secret_export_rejects_duplicates(self):
        with self.assertRaises(RuntimeError): dev.decode_export('{"KEY":"a", "key":"b"}')

    def authentication_bundle(self):
        values = {"SUPABASE_DATABASE_PASSWORD": "bootstrap", "PROBE_PASSWORD": "p" * 32,
                  "RUNTIME_PASSWORD": "r" * 32, "MIGRATION_PASSWORD": "m" * 32,
                  "IdentityAccess__Otp__HashKeys__v1": "otp", "IdentityAccess__Otp__CurrentHashKeyVersion": "v1",
                  "Platform__RateLimiting__PartitionDigestKey": "partition"}
        for purpose, key in [("probe", "RuntimeProbeDatabase"), ("runtime", "IdentityAccessDatabase"), ("migration", "IdentityAccessMigrationDatabase")]:
            values["ConnectionStrings__" + key] = dev.connection_string(self.target(), values[purpose.upper() + "_PASSWORD"], purpose)
        return values

    def test_authentication_missing_rejected_except_explicit_additive_provision_boundary(self):
        values = self.authentication_bundle()
        with patch.object(dev, "database_target", return_value=self.target()), patch.object(dev, "export", return_value=values):
            with self.assertRaisesRegex(RuntimeError, "AuthenticationBundleMissing"): dev.bundle("Development")
            self.assertEqual(values, dev.bundle("Development", authentication_required=False)[1])

    def test_partial_authentication_never_accepted_or_overwritten(self):
        values = self.authentication_bundle()
        values["IdentityAccess__Jwt__PrivateKeyPem"] = "private-marker"
        with patch.object(dev, "database_target", return_value=self.target()), patch.object(dev, "export", return_value=values), patch.object(dev, "cli") as cli:
            with self.assertRaises(RuntimeError): dev.provision_authentication()
            cli.assert_not_called()

    def test_existing_authentication_only_validated_and_not_replaced(self):
        import contextlib, io
        values = self.authentication_bundle()
        values.update({"IdentityAccess__Jwt__PrivateKeyPem": "private-marker", "IdentityAccess__Jwt__CurrentKeyId": "old",
                       "IdentityAccess__Jwt__ValidationPublicKeys__old": "public-marker"})
        with patch.object(dev, "database_target", return_value=self.target()), patch.object(dev, "export", return_value=values), \
                patch.object(dev, "cli") as cli, patch.object(dev, "command", return_value="public-marker") as command, \
                contextlib.redirect_stdout(io.StringIO()) as output:
            dev.provision_authentication()
            cli.assert_not_called()
            self.assertNotIn("private-marker", output.getvalue())
            self.assertNotIn("private-marker", repr(command.call_args.args))

    def test_export_preserves_multiline_key_values(self):
        import json
        secret = "-----BEGIN PRIVATE KEY-----\nmarker\n-----END PRIVATE KEY-----\n"
        self.assertEqual(secret, dev.decode_export(json.dumps({"IdentityAccess__Jwt__PrivateKeyPem": secret}))["IdentityAccess__Jwt__PrivateKeyPem"])

    def test_new_authentication_uses_private_yaml_and_preserves_existing_secrets(self):
        import contextlib, io, json, stat
        values = self.authentication_bundle()
        private = "-----BEGIN PRIVATE KEY-----\nsynthetic\n-----END PRIVATE KEY-----\n"
        public = "-----BEGIN PUBLIC KEY-----\nsynthetic\n-----END PUBLIC KEY-----\n"
        updated = values | {"IdentityAccess__Jwt__PrivateKeyPem": private.rstrip("\n"), "IdentityAccess__Jwt__CurrentKeyId": "s03-dev-v1",
                            "IdentityAccess__Jwt__ValidationPublicKeys__s03-dev-v1": public.rstrip("\n")}
        files = []
        def inspect(arguments):
            file = Path(next(argument[7:] for argument in arguments if argument.startswith("--file=")))
            self.assertEqual(".yaml", file.suffix)
            self.assertEqual(0o600, stat.S_IMODE(file.stat().st_mode))
            document = json.loads(file.read_text())
            self.assertEqual(set(updated) - set(values), set(document))
            self.assertEqual(private.rstrip("\n"), document["IdentityAccess__Jwt__PrivateKeyPem"])
            files.append(file)
            return ""
        with patch.object(dev, "database_target", return_value=self.target()), patch.object(dev, "export", side_effect=[values, updated]), \
                patch.object(dev, "cli", side_effect=inspect), patch.object(dev, "command", side_effect=[private, public, public]), \
                contextlib.redirect_stdout(io.StringIO()) as output:
            dev.provision_authentication()
            self.assertNotIn("synthetic", output.getvalue())
        self.assertTrue(files and all(not file.exists() for file in files))
