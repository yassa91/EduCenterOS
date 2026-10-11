"""Risk checks for CI discovery and safe publication, using synthetic reports."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from xml.sax.saxutils import escape

SPEC = importlib.util.spec_from_file_location("verification", Path(__file__).resolve().parents[2] / "scripts/verify.py")
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class ReportGateTests(unittest.TestCase):
    def report(self, *, total=1, executed=1, passed=1, failed=0, skipped=0, outcome="Passed", include_result=True,
               error_message="diagnostic-sensitive-marker"):
        result = (f'<UnitTestResult testName="EduCenterOS.UnitTests.Example.Scenario(input: diagnostic-sensitive-marker)" outcome="{outcome}">'
                  f'<Output><ErrorInfo><Message>{escape(error_message)}</Message></ErrorInfo><StdOut>diagnostic-sensitive-marker</StdOut></Output></UnitTestResult>') if include_result else ""
        return (f'<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>{result}</Results>'
                f'<ResultSummary><Counters total="{total}" executed="{executed}" passed="{passed}" failed="{failed}" notExecuted="{skipped}" /></ResultSummary></TestRun>')

    def parse(self, content):
        with tempfile.TemporaryDirectory() as directory:
            report = Path(directory) / "synthetic.trx"
            report.write_text(content)
            return MODULE.safe_report(report, "Unit")

    def test_zero_discovery_is_rejected(self):
        with self.assertRaises(ValueError):
            self.parse(self.report(total=0, executed=0, passed=0, include_result=False))

    def test_failed_case_keeps_gate_failed(self):
        report = self.parse(self.report(passed=0, failed=1, outcome="Failed"))
        self.assertFalse(report["gatePassed"])

    def test_skipped_case_keeps_gate_failed(self):
        report = self.parse(self.report(executed=0, passed=0, skipped=1, outcome="NotExecuted"))
        self.assertFalse(report["gatePassed"])

    def test_report_counters_cannot_hide_missing_results(self):
        with self.assertRaises(ValueError):
            self.parse(self.report(total=2))

    def test_counters_cannot_hide_a_failed_outcome(self):
        with self.assertRaises(ValueError):
            self.parse(self.report(outcome="Failed"))

    def test_published_projection_excludes_inputs_and_raw_failure_output(self):
        report = self.parse(self.report())
        self.assertTrue(report["gatePassed"])
        self.assertNotIn("diagnostic-sensitive-marker", json.dumps(report))
        self.assertEqual("EduCenterOS.UnitTests.Example.Scenario", report["tests"][0]["test"])

    def test_failed_fixture_diagnostic_emits_only_an_allowlisted_category(self):
        report = self.parse(self.report(passed=0, failed=1, outcome="Failed", error_message=
            "System.InvalidOperationException : TestSafety.CloudProjectAlreadyInUse diagnostic-sensitive-marker"))
        self.assertFalse(report["gatePassed"])
        self.assertEqual("TestSafety.CloudProjectAlreadyInUse", report["tests"][0]["failureCategory"])
        self.assertNotIn("diagnostic-sensitive-marker", json.dumps(report))

    def test_known_transport_diagnostics_are_static_and_unknown_text_is_withheld(self):
        cases = [
            ("System.Net.Sockets.SocketException : diagnostic-sensitive-marker", "Network.SocketFailure"),
            ("Npgsql.NpgsqlException : diagnostic-sensitive-marker", "Database.TransportFailure"),
            ("Npgsql.PostgresException : diagnostic-sensitive-marker", "Database.ServerFailure"),
            ("System.TimeoutException : diagnostic-sensitive-marker", "Execution.Timeout"),
            ("UnknownException : diagnostic-sensitive-marker", "Unclassified"),
            ("diagnostic-sensitive-marker TestSafety.CloudProjectAlreadyInUse", "Unclassified")
        ]
        for message, expected in cases:
            with self.subTest(category=expected):
                report = self.parse(self.report(passed=0, failed=1, outcome="Failed", error_message=message))
                self.assertEqual(expected, report["tests"][0]["failureCategory"])
                self.assertFalse(report["gatePassed"])
                self.assertNotIn("diagnostic-sensitive-marker", json.dumps(report))

    def test_passing_case_never_publishes_failure_diagnostics(self):
        report = self.parse(self.report(error_message="System.TimeoutException : diagnostic-sensitive-marker"))
        self.assertTrue(report["gatePassed"])
        self.assertNotIn("failureCategory", report["tests"][0])


if __name__ == "__main__":
    unittest.main()
