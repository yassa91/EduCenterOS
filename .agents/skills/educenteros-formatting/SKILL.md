---
name: educenteros-formatting
description: Format or review handwritten C# in EduCenterOS using its approved readability rules and repository formatter. Use for C# formatting, indentation, braces, line wrapping, statement grouping, attributes, and Error argument layout, including requests to format code or improve its layout. Exclude structural refactoring, renaming, behavior changes, and generated EF code.
---

# EduCenterOS Formatting

Produce readable C# with the existing behavior preserved. Apply this skill when
writing or editing handwritten C# in this repository as well as for an explicit
formatting request. A request for review or explanation authorizes inspection,
not source edits.

## Establish the repository contract

Work from the current EduCenterOS checkout. Resolve the repository root before
running commands; the skill directory is not the working directory.

This skill and its bundled resources are the sole formatting authority.
Do not introduce rules in `AGENTS.md`, technical documents, other skills, or a
root `.editorconfig`. Change formatting policy here. Existing source files are
examples of application, not independent style authorities.

Read the applicable [AGENTS.md](../../../AGENTS.md) for general project
instructions and `global.json` for the pinned SDK. A formatting task does not
authorize an SDK upgrade. The SDK/editor settings are bundled in
[assets/editorconfig](assets/editorconfig); keep those settings and the
[formatter implementation](scripts/formatter/CodeFormatter.cs) consistent with
this skill when changing its policy. The settings file is not automatically
loaded by editors for project source; use the skill's formatter by default.

Inspect Git status and both staged and unstaged diffs. Establish which files and
edits already belong to the user, including untracked files. Retain the original
contents of files you will edit so the final review distinguishes existing work
from this task. Do not reset, stash, stage, or commit unrelated changes.

## Apply the readability rules

- Use four spaces, LF, and braces on their own lines for block bodies.
- Write one statement per line. Keep compact auto-properties and short
  single-statement guards on one line where appropriate.
- Separate methods, constructors, and types with a blank line. Keep related
  fields and properties together instead of inserting a blank line after each.
- Group validation guards, state updates, and the final return with blank lines
  where that makes the sequence easier to follow.
- Wrap long conditions and parameter lists according to the surrounding code.
  Put `&&` and `||` at the ends of continued condition lines. For a multiline
  `if`, start the condition on the line after `if (` and align its closing `)`
  with `if`. Do not invent a fixed line-length limit.
- Put each attribute list on its own line.
- Put every `Error` constructor argument on its own line, including target-typed
  `new(...)`, explicit `new Error(...)`, retry metadata, and validation issues.
  Align the closing parenthesis with the enclosing declaration or statement.

Read [layout-examples.md](references/layout-examples.md) when applying these
layouts. Treat the examples as layout guidance; retain the actual code's values
and behavior.

Change whitespace only in a formatting task. Preserve token order, identifiers,
signatures, conditions, evaluation order, literals, messages, comments, and
directives. Whitespace inside strings, raw strings, and comments is content.
Do not rename, extract methods, simplify expressions, remove `using` statements,
or add/remove braces just to obtain a different layout. Refactoring and analyzer
fixes are separate work and need their own authorized scope.

Generated EF migrations and snapshots keep their generated format. Exclude build
outputs and generated source; do not weaken exclusions to make a check pass.

## Choose the mode and scope

Run these commands from the repository root.

**Review or verification:** inspect the requested files and run the non-mutating
source check. Report concrete locations needing manual layout review as well as
the check result. Do not run `--fix` or another mutating formatter.

```sh
dotnet run --project tools/EduCenterOS.Formatting -c Release -- --check
```

**Apply to selected files:** make whitespace-only edits in those files, then run
the same check. The current formatter scans all handwritten C# under `src`,
`tests`, `tools`, and this skill's `scripts/formatter`; it has no `--include` or
single-file mode. Report unrelated
violations separately rather than expanding the edit scope. A repository-wide
check failure remains a failure even when the selected files are clean.

**Apply across the repository:** when the user has authorized repository-wide
formatting, use the canonical fix command. Inspect its full changed-file list
against the baseline, review every changed file, and complete manual line wrapping
and meaningful statement grouping where needed.

```sh
dotnet run --project tools/EduCenterOS.Formatting -c Release -- --fix
```

The formatter sources and their self-tests live in `scripts/formatter` inside
this skill. `tools/EduCenterOS.Formatting` only builds those linked sources so
the existing solution and CI commands continue working without copied rules.
Its parser-based guard
checks that its own transformations preserve tokens, comments, directives, and
literal contents. That guard does not verify independent manual edits or business
correctness. Review those edits explicitly and retain the required build/tests.

## Optional SDK formatter

Do not replace the repository formatter with `dotnet format`. If the user
explicitly requests an SDK formatter operation, first establish which rules and
files it owns alongside the repository tool. See
[sdk-format.md](references/sdk-format.md) for scoped commands and diagnostics.
Never run `style`, `analyzers`, or all formatter surfaces as an implicit part of a
whitespace-only task. Always finish with the canonical project check.

## Validate and deliver

Run the canonical `--check` after the last edit, plus `git diff --check`. Compare
the resulting files with the original contents; review all changes for scope,
generated-file exclusions, and preserved behavior. Do not treat a whitespace
stripping comparison as proof: it can hide edits inside literal or comment text.

Run the applicable build and behavioral tests under
[T33](../../../docs/technical/T33%20-%20Testing%20Stack.md). If the formatter itself
changes, also run its `--self-test`. For task delivery requiring the full release
gate, keep [scripts/verify.py](../../../scripts/verify.py) and the requirements in
[T40](../../../docs/technical/T40%20-%20Git%20%26%20GitHub%20Workflow.md). Do not
bypass failed checks, missing test prerequisites, or cloud test ownership guards.

On failure, diagnose the reported problem and retry only after a relevant fix.
Keep command exit codes visible and diagnostics concise. Do not repeatedly run a
formatter expecting it to implement manual grouping or fix an unsupported rule.

Report the files changed, the useful layout improvements, the commands actually
run and their results, and any remaining failures or unverified checks. Never
describe formatting success as evidence that business logic is correct.

## Origin

The scope, diff-review, formatter-ownership, and verification workflow is adapted
from [managedcode/dotnet-skills: format](https://github.com/managedcode/dotnet-skills/blob/main/catalog/Tools/Format/skills/format/SKILL.md).
The C# layout contract and primary commands come from EduCenterOS. The adapted
upstream material retains its [MIT notice](LICENSE). This skill is local to the
project and needs no other installed skills or online instruction lookup. Normal
SDK restore and build prerequisites still apply.
