# Optional dotnet format operations

Use this reference only for an explicitly requested SDK formatter operation.
The canonical EduCenterOS check remains mandatory. Establish formatter ownership
and the exact authorized paths before using these examples; they are not the
default commands for this skill.

## Whitespace verification and application

From the repository root, use the bundled helper for scoped whitespace
verification:

```sh
python3 .agents/skills/educenteros-formatting/scripts/sdk_whitespace.py \
  --include src/EduCenterOS.Modules.IdentityAccess/Domain/OtpChallenge.cs
```

When formatting that file is authorized, add `--apply`. The helper temporarily
loads [the skill's settings](../assets/editorconfig) as a root `.editorconfig`
for the SDK, then removes that temporary copy on completion or failure. It
refuses to overwrite a pre-existing configuration and preserves a copy changed
concurrently for review. Never commit or maintain a separate root configuration.

`--include` accepts repository-relative C# files or folders, not shell glob
expressions. Folder selections expand to handwritten C# only; generated EF and
build outputs are excluded. Use the user's actual selected paths.

The formatter may restore, load project code, and run analyzers. Use only the
current trusted checkout and pinned SDK. Use `--no-restore` only after a successful
restore for that checkout. Keep the command's actual exit code and inspect output;
successful execution does not imply every diagnostic has an automatic fix.

## Style or analyzer fixes

These alter syntax and exceed a whitespace-only formatting task. Use them only
when the user has separately authorized that cleanup. Prefer an explicit rule ID
and selected paths over a solution-wide sweep. For example, removing unnecessary
imports uses `style --diagnostics IDE0005 --severity info`; non-style analyzer fixes
use `analyzers --diagnostics` with the requested rule ID.

Do not change the skill's settings, analyzer severities, or the SDK to silence failures.
For separately authorized SDK style/analyzer cleanup, use this same temporary
settings lifecycle and cleanup discipline; the helper supports whitespace only.
Review every changed file and run the applicable build and behavioral tests.

## Final verification and CI

After any authorized SDK operation, inspect the diff and run:

```sh
dotnet run --project tools/EduCenterOS.Formatting -c Release -- --check
git diff --check
```

Do not infer that `dotnet format` enforces this skill's Error argument layout or meaningful
statement grouping. Apply approved manual whitespace adjustments as needed.

The existing CI already checks formatting through `scripts/verify.py`. A formatting
task does not authorize replacing that gate or adding a competing one. If a CI
change is requested, keep CI checks non-mutating; do not make PR jobs apply and
commit formatting fixes automatically.

## Sources

- [Managed Code format workflow](https://github.com/managedcode/dotnet-skills/blob/main/catalog/Tools/Format/skills/format/SKILL.md)
- [dotnet format command reference](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-format)
- [C# formatting options](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/csharp-formatting-options)
