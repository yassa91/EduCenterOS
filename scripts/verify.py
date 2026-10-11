#!/usr/bin/env python3
"""Release gate: isolated cloud integration tests and allowlisted result evidence."""
import json
from collections import Counter
import os
from pathlib import Path
import re
import subprocess
import sys
import uuid
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
SUITES = ("Unit", "Integration", "Architecture")
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def failure_category(item):
    # Match only fixed exception identities/codes; never publish their messages or values.
    message = item.findtext("t:Output/t:ErrorInfo/t:Message", default="", namespaces=NS)
    safety_codes = (
        "TestSafety.CloudProjectAlreadyInUse", "TestSafety.TrustedCloudSnapshotRequired",
        "TestSafety.ActualIdentityMismatch", "TestSafety.TargetNotOwned",
        "TestSafety.LeaseSessionLost", "TestSafety.LeaseMismatch"
    )
    for code in safety_codes:
        if re.search(r"\bSystem\.InvalidOperationException\s*:\s*" + re.escape(code) + r"(?![A-Za-z0-9_.])", message):
            return code
    exception_categories = (
        ("System.Net.Sockets.SocketException", "Network.SocketFailure"),
        ("System.TimeoutException", "Execution.Timeout"),
        ("Npgsql.PostgresException", "Database.ServerFailure"),
        ("Npgsql.NpgsqlException", "Database.TransportFailure")
    )
    for exception_type, category in exception_categories:
        if re.search(r"\b" + re.escape(exception_type) + r"\s*:", message):
            return category
    return "Unclassified"


def safe_report(report, suite):
    document = ET.parse(report).getroot()
    counters = document.find("t:ResultSummary/t:Counters", NS)
    results = document.findall("t:Results/t:UnitTestResult", NS)
    if counters is None or not results:
        raise ValueError("Verification.MissingDiscovery")
    totals = {name: int(counters.attrib[name]) for name in ("total", "executed", "passed", "failed", "notExecuted")}
    if totals["total"] != len(results) or totals["executed"] != sum(item.attrib["outcome"] != "NotExecuted" for item in results):
        raise ValueError("Verification.InconsistentReport")
    tests = []
    for item in results:
        # Dynamic theory inputs, raw error text, stdout, paths and attachments never enter published evidence.
        name = item.attrib["testName"].split("(", 1)[0]
        if not re.fullmatch(r"EduCenterOS\.[A-Za-z0-9_.]+", name):
            raise ValueError("Verification.UnsafeTestName")
        outcome = item.attrib["outcome"]
        if outcome not in ("Passed", "Failed", "NotExecuted"):
            raise ValueError("Verification.UnexpectedOutcome")
        entry = {"test": name, "outcome": outcome}
        if outcome == "Failed": entry["failureCategory"] = failure_category(item)
        tests.append(entry)
    for counter, outcome in (("passed", "Passed"), ("failed", "Failed"), ("notExecuted", "NotExecuted")):
        if totals[counter] != sum(item["outcome"] == outcome for item in tests):
            raise ValueError("Verification.InconsistentOutcomes")
    passed = (totals["total"] > 0 and totals["passed"] == totals["total"]
              and totals["executed"] == totals["total"] and totals["failed"] == 0 and totals["notExecuted"] == 0)
    return {"suite": suite, "counts": totals, "gatePassed": passed, "tests": tests}


def main():
    run = ROOT / "artifacts/test-results" / uuid.uuid4().hex
    run.mkdir(parents=True, mode=0o700)
    safe = run / "safe"
    safe.mkdir(mode=0o700)
    env = {key: os.environ[key] for key in ("PATH", "HOME", "USER", "TMPDIR", "DOTNET_ROOT") if key in os.environ}
    env.update(DOTNET_ENVIRONMENT="Testing", ASPNETCORE_ENVIRONMENT="Testing",
               DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")

    def command(args, filename, timeout):
        # Child output stays private: even a failing test must not dump arbitrary inputs into CI logs.
        target = run / filename
        descriptor = os.open(target, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(descriptor, "w") as output:
            try:
                result = subprocess.run(args, cwd=ROOT, env=env, stdout=output, stderr=subprocess.STDOUT, timeout=timeout)
            except subprocess.TimeoutExpired:
                # Only the internal stage label and deadline are public; command output stays private.
                print(f"Execution deadline reached during {filename.removesuffix('.log')} ({timeout}s); no automatic retry or skip.", flush=True)
                raise
        return result.returncode == 0

    for stage, args, timeout in (
        ("hygiene", ["git", "diff", "--check"], 30),
        ("report-guard", [sys.executable, "-m", "unittest", "discover", "-s", "tests/verification"], 30),
        ("restore", ["dotnet", "restore", "EduCenterOS.sln", "--locked-mode"], 300),
        ("build", ["dotnet", "build", "EduCenterOS.sln", "-c", "Release", "--no-restore"], 180),
        ("formatter-tests", ["dotnet", "run", "--project", "tools/EduCenterOS.Formatting", "-c", "Release", "--no-build", "--", "--self-test"], 30),
        ("formatting", ["dotnet", "run", "--project", "tools/EduCenterOS.Formatting", "-c", "Release", "--no-build", "--", "--check"], 30),
    ):
        if not command(args, stage + ".log", timeout):
            print(f"Verification failed during {stage}. Private diagnostics: {run.relative_to(ROOT)}")
            return 1
        print(f"{stage}: passed", flush=True)

    # A hosted CI runner receives only restricted test-project credentials from a GitHub secret.
    # On a developer machine fetch the test scope from Infisical; never pass it to restore/build/unit tests.
    snapshot = os.environ.get("EDUCENTEROS_TEST_DATABASE_SNAPSHOT")
    if snapshot is None:
        import dev
        snapshot = dev.test_snapshot()
    else:
        if not snapshot or len(snapshot) > 16384: raise ValueError("Verification.InvalidCloudSnapshot")
        document = json.loads(snapshot)
        for key in ("owner", "probe", "runtime"):
            connection = document[key]
            certificate = ";Root Certificate=__EDUCENTEROS_SUPABASE_CA__"
            if certificate not in connection: raise ValueError("Verification.CICertificateBindingMissing")
            document[key] = connection.replace(certificate, ";Root Certificate=" + str(ROOT / "infra/supabase-ca.crt"))
        snapshot = json.dumps(document)
    success = True
    for suite in SUITES:
        if suite == "Integration": env["EDUCENTEROS_TEST_DATABASE_SNAPSHOT"] = snapshot
        else: env.pop("EDUCENTEROS_TEST_DATABASE_SNAPSHOT", None)
        project = f"tests/EduCenterOS.{suite}Tests/EduCenterOS.{suite}Tests.csproj"
        destination = run / suite
        # The expanded S03 cloud suite exhausted its former twenty-minute deadline.
        # Preserve every mandatory case and a finite thirty-minute allowance.
        suite_timeout = 1800 if suite == "Integration" else 600
        completed = command(["dotnet", "test", project, "-c", "Release", "--no-build", "--no-restore",
                             "--logger", "trx;LogFileName=results.trx", "--results-directory", str(destination)], suite + ".log", suite_timeout)
        try:
            report = safe_report(destination / "results.trx", suite)
        except (OSError, ET.ParseError, ValueError, KeyError):
            print(f"{suite}: missing, inconsistent or unsafe test report")
            success = False
            continue
        (safe / (suite + ".json")).write_text(json.dumps(report, indent=2) + "\n")
        counts = report["counts"]
        print(f"{suite}: total={counts['total']} passed={counts['passed']} failed={counts['failed']} skipped={counts['notExecuted']}", flush=True)
        if counts["failed"]:
            categories = Counter(item["failureCategory"] for item in report["tests"] if item["outcome"] == "Failed")
            print(suite + " failure categories: " + ", ".join(f"{category}={count}" for category, count in sorted(categories.items())), flush=True)
        success = success and completed and report["gatePassed"]
    print(f"Safe evidence: {safe.relative_to(ROOT)}")
    return 0 if success else 1


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, subprocess.TimeoutExpired, ValueError, KeyError, RuntimeError):
        print("Verification prerequisite or execution failed; no automatic retry or skip.")
        sys.exit(1)
