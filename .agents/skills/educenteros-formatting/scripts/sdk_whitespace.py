#!/usr/bin/env python3
"""Run explicitly requested SDK whitespace formatting with this skill's settings."""

import argparse
from pathlib import Path
import subprocess
import sys


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--include", nargs="+", required=True, help="Repository-relative C# paths or folders")
    parser.add_argument("--apply", action="store_true", help="Apply instead of verifying")
    parser.add_argument("--no-restore", action="store_true", help="Use a previously restored checkout")
    args = parser.parse_args()

    skill = Path(__file__).resolve().parent.parent
    root = skill.parents[2]
    if not (root / "EduCenterOS.sln").is_file():
        parser.error("Run this helper from its EduCenterOS skill installation.")

    allowed = [root / folder for folder in ("src", "tests", "tools")]
    allowed.append(skill / "scripts/formatter")
    excluded = {"bin", "obj", "Migrations"}
    selected = set()
    for value in args.include:
        path = (root / value).resolve()
        if not path.exists() or not any(path.is_relative_to(folder) for folder in allowed):
            parser.error(f"Path is missing or outside handwritten C# scope: {value}")
        candidates = path.rglob("*.cs") if path.is_dir() else [path]
        for candidate in candidates:
            candidate = candidate.resolve()
            if not any(candidate.is_relative_to(folder) for folder in allowed):
                parser.error(f"Selected source resolves outside handwritten C# scope: {value}")
            relative = candidate.relative_to(root)
            if candidate.suffix != ".cs" or excluded.intersection(relative.parts):
                if path.is_file():
                    parser.error(f"Select handwritten C#; generated/build files are excluded: {value}")
                continue
            selected.add(relative.as_posix())
    if not selected:
        parser.error("No handwritten C# files selected.")

    command = ["dotnet", "format", "whitespace", "EduCenterOS.sln"]
    if not args.apply:
        command.append("--verify-no-changes")
    if args.no_restore:
        command.append("--no-restore")
    command.extend(["--include", *sorted(selected)])

    settings = (skill / "assets/editorconfig").read_bytes()
    temporary_config = root / ".editorconfig"
    try:
        # Exclusive creation preserves any configuration added by another task.
        with temporary_config.open("xb") as target:
            target.write(settings)
    except FileExistsError:
        parser.error("Root .editorconfig already exists; preserve it and resolve the competing configuration first.")

    try:
        return subprocess.run(command, cwd=root, check=False).returncode
    finally:
        if temporary_config.read_bytes() == settings:
            temporary_config.unlink()
        else:
            raise RuntimeError("Temporary .editorconfig changed during the run; preserved for review.")


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, RuntimeError) as error:
        print(f"SDK formatting: {error}", file=sys.stderr)
        sys.exit(2)
