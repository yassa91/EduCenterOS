# DB01 self-review

Review scope: cloud connection policy, bootstrap/migrations, cloud test cleanup, CI secret boundary.

- Local PostgreSQL/transaction pooler/TLS downgrade/wrong environment or role are rejected before open.
- Actual server identity/major/schema owner guard remains in migrations; API receives no migration/admin secrets.
- Test target is a separately trusted project with a postgres-owned marker readable (not writable) by test owner.
- Exclusive live session lock and random fixture lease guard reviewed schema cleanup; wrong principal and
  invalid lease tests preserve sentinel data. No database-wide drop or managed-schema cleanup.
- Migration upgrade scenario reuses its fixture lease instead of nesting competing cloud fixtures.
- Refused endpoint fault affects only one TestServer readiness probe and is restored, preserving cloud availability.
- CI gets only restricted test project credentials, only during Integration execution; logs/TRX stay private,
  uploaded evidence contains allowlisted test names/counts/outcomes.
- The public provider CA is a non-secret reviewed artifact; CI substitutes a CA marker with its local checkout
  path, preserving VerifyFull rather than depending on a workstation-specific path or disabling verification.
- Old local credentials/volume and unrelated uncommitted changes are preserved for rollback.

Evidence: initial full cloud gate 102/92/5 with zero failed/skipped; Python guards 11.
Native Supabase flow returned 200/200/201 and SQL-confirmed persistence; OTP removed on verify.
A later provenance clarification changed Testing source to CloudTestFixture; final PR-head CI must cover it.
