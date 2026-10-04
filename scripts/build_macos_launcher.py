#!/usr/bin/env python3
"""Build a local macOS .app for this checkout; generated binaries stay out of Git."""
import argparse
from pathlib import Path
import plistlib
import platform
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT / ".local/mac-launcher/EduCenterOS.app")
    args = parser.parse_args()
    if sys.platform != "darwin":
        parser.error("This launcher requires macOS.")
    bundle = args.output.resolve()
    if bundle.suffix != ".app":
        parser.error("Output must end in .app.")
    contents = bundle / "Contents"
    executable = contents / "MacOS/EduCenterOSLauncher"
    executable.parent.mkdir(parents=True, exist_ok=True)
    (contents / "Resources").mkdir(exist_ok=True)
    target = platform.machine() + "-apple-macosx13.0"
    subprocess.run(["xcrun", "swiftc", str(ROOT / "scripts/macos/Launcher.swift"), "-target", target, "-O", "-o", str(executable)], check=True)
    metadata = {
        "CFBundleIdentifier": "com.educenteros.local-launcher",
        "CFBundleName": "EduCenterOS",
        "CFBundleDisplayName": "EduCenterOS",
        "CFBundleExecutable": executable.name,
        "CFBundlePackageType": "APPL",
        "CFBundleVersion": "1",
        "CFBundleShortVersionString": "1.0",
        "LSMinimumSystemVersion": "13.0",
        "LSMultipleInstancesProhibited": True,
        "NSHighResolutionCapable": True,
        "EduCenterOSProjectRoot": str(ROOT),
    }
    with (contents / "Info.plist").open("wb") as output:
        plistlib.dump(metadata, output)
    executable.chmod(0o755)
    subprocess.run(["/usr/bin/codesign", "--force", "--sign", "-", str(bundle)], check=True)
    print(bundle)


if __name__ == "__main__":
    main()
