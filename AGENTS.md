# Working in EduCenterOS

## Readable C# is required

Use `.editorconfig`, `docs/technical/T42 - Code Formatting.md`, and the current
`src/EduCenterOS.Modules.IdentityAccess/Domain/OtpChallenge.cs` as the style references.
Apply these rules to new and edited handwritten C# in `src`, `tests`, and `tools`:

- Use four spaces, LF line endings, and braces on their own lines for block bodies.
- Put each statement on a separate line. Keep compact auto-properties on one line.
- Separate methods, constructors, and types with a blank line. Group related fields
  and properties without a blank line between every property.
- Separate validation guards, state updates, and the final return where it improves
  readability. A short single-statement guard may remain on one line.
- Split long conditions across indented lines, with `&&`/`||` at the end of each
  continued line. For a multiline `if`, place the opening condition after `if (`
  on the next line and align the closing `)` with `if`.
- Put each attribute list on its own line.
- Write every `Error` construction with one argument per line and its closing
  parenthesis aligned with the enclosing declaration or statement. For example:

```csharp
    internal static Error VerificationRejected => new(
        "IdentityAccess.VerificationRejected",
        ErrorCategory.BusinessRule,
        "Verification could not be completed."
    );
```

Do not change evaluation order, conditions, literals, messages, signatures, or
behavior just to obtain a different layout. Avoid logic changes in formatting
commits. Generated EF migrations and snapshots keep their generated format.

Before delivery, run the formatting check and the applicable build/tests. The
formatting check verifies readability rules; it does not prove business logic is
correct. Keep the existing verification gates and relevant behavioral tests.

```sh
dotnet run --project tools/EduCenterOS.Formatting -c Release -- --check
```

To apply the mechanically checked whitespace rules:

```sh
dotnet run --project tools/EduCenterOS.Formatting -c Release -- --fix
```

Follow `docs/technical/T40 - Git & GitHub Workflow.md` for delivery. Preserve local
work, keep secrets out of commits, and do not push directly to `main`.
