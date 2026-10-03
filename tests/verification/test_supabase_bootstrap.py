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
