# T42 - Code Formatting

Approved by the project owner on 2026-10-04 for existing and future handwritten C#.

The current `OtpChallenge.cs` is the readability reference. Formatting preserves
the existing logic and keeps declarations and execution steps easy to scan.

- Use four spaces, LF, and block braces on separate lines.
- Write one statement per line, while keeping short guards and auto-properties compact.
- Add a blank line between methods, constructors, and types. Keep related properties together.
- Separate validation, state changes, and returns into readable groups.
- Wrap long conditions and parameter lists. In multiline conditions, put logical
  operators at the end of continued lines and align the closing parenthesis with `if`.
- Put each attribute list on a separate line.
- Put each `Error` constructor argument on its own line, regardless of line length:

```csharp
internal static Error VerificationRejected => new(
    "IdentityAccess.VerificationRejected",
    ErrorCategory.BusinessRule,
    "Verification could not be completed."
);
```

The same argument layout applies to `new Error(...)` inside handlers and tests,
including optional retry arguments and validation issues produced by error factories.
Error codes, categories, messages, and behavior remain unchanged.

`.editorconfig` supplies editor settings. `AGENTS.md` supplies instructions for
future coding agents. The repository formatter uses the C# parser from the pinned
.NET SDK to check statement separation, member spacing, attributes, block layout,
and error argument layout. It changes whitespace only and verifies that syntax
tokens, comments, directives, and literal contents are preserved. Generated EF
migrations, snapshots, and build outputs are excluded.

```sh
dotnet run --project tools/EduCenterOS.Formatting -c Release -- --check
dotnet run --project tools/EduCenterOS.Formatting -c Release -- --fix
dotnet run --project tools/EduCenterOS.Formatting -c Release -- --self-test
```

Run `--fix` explicitly; `--check` never rewrites source files. CI runs the formatter
tests and formatting check through `scripts/verify.py`, along with the existing
build and behavioral test suites. Line wrapping and meaningful statement grouping
still require review: formatting alone cannot establish correct business logic.
