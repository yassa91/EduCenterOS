#!/usr/bin/env python3
"""Release gate without runtime secrets; publish only an allowlisted result projection."""
import json
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
        # Dynamic theory inputs, error text, stdout, paths and attachments never enter published evidence.
        name = item.attrib["testName"].split("(", 1)[0]
        if not re.fullmatch(r"EduCenterOS\.[A-Za-z0-9_.]+", name):
            raise ValueError("Verification.UnsafeTestName")
        outcome = item.attrib["outcome"]
        if outcome not in ("Passed", "Failed", "NotExecuted"):
            raise ValueError("Verification.UnexpectedOutcome")
        tests.append({"test": name, "outcome": outcome})
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
    manifest = json.loads((ROOT / "infra/runtime.json").read_text())
    env.update(DOTNET_ENVIRONMENT="Testing", ASPNETCORE_ENVIRONMENT="Testing",
               DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1",
               TESTCONTAINERS_RYUK_CONTAINER_IMAGE=manifest["resourceReaperImage"],
               TESTCONTAINERS_RYUK_DISABLED="false", TESTCONTAINERS_WAIT_STRATEGY_TIMEOUT="00:01:00")

    def command(args, filename, timeout):
        # Child output stays private: even a failing test must not dump arbitrary inputs into CI logs.
        target = run / filename
        descriptor = os.open(target, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(descriptor, "w") as output:
            result = subprocess.run(args, cwd=ROOT, env=env, stdout=output, stderr=subprocess.STDOUT, timeout=timeout)
        return result.returncode == 0

    for stage, args, timeout in (
        ("hygiene", ["git", "diff", "--check"], 30),
        ("report-guard", [sys.executable, "-m", "unittest", "discover", "-s", "tests/verification"], 30),
        ("restore", ["dotnet", "restore", "EduCenterOS.sln", "--locked-mode"], 300),
        ("build", ["dotnet", "build", "EduCenterOS.sln", "-c", "Release", "--no-restore"], 180),
        ("docker", ["docker", "info", "--format", "{{.ServerVersion}}"], 30),
        ("postgres-image", ["docker", "pull", manifest["postgresImage"]], 180),
        ("reaper-image", ["docker", "pull", manifest["resourceReaperImage"]], 180),
    ):
        if not command(args, stage + ".log", timeout):
            print(f"Verification failed during {stage}. Private diagnostics: {run.relative_to(ROOT)}")
            return 1
        print(f"{stage}: passed", flush=True)

    success = True
    for suite in SUITES:
        project = f"tests/EduCenterOS.{suite}Tests/EduCenterOS.{suite}Tests.csproj"
        destination = run / suite
        completed = command(["dotnet", "test", project, "-c", "Release", "--no-build", "--no-restore",
                             "--logger", "trx;LogFileName=results.trx", "--results-directory", str(destination)], suite + ".log", 180)
        try:
            report = safe_report(destination / "results.trx", suite)
        except (OSError, ET.ParseError, ValueError, KeyError):
            print(f"{suite}: missing, inconsistent or unsafe test report")
            success = False
            continue
        (safe / (suite + ".json")).write_text(json.dumps(report, indent=2) + "\n")
        counts = report["counts"]
        print(f"{suite}: total={counts['total']} passed={counts['passed']} failed={counts['failed']} skipped={counts['notExecuted']}", flush=True)
        success = success and completed and report["gatePassed"]
    print(f"Safe evidence: {safe.relative_to(ROOT)}")
    return 0 if success else 1


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, subprocess.TimeoutExpired, ValueError, KeyError):
        print("Verification prerequisite or execution failed; no automatic retry or skip.")
        sys.exit(1)
