# S02 registration contract

Status: implementation baseline approved within S02 scope on 2026-10-01. The numeric settings below are reviewed **local development/testing baselines**, not a production release policy. Owners: T12, T31/T32, T35/T36. Login, linking, real SMS and institution access remain outside this contract.

## HTTP operations

All routes are `AnonymousSecurity`, explicitly anonymous, JSON only, case-sensitive camelCase, unknown/duplicate fields rejected; no query parameters. Maximum body: 16,384 UTF-8 bytes. No body/credential logging. Responses have no-store and server correlation. JSON request types are allowlists, not entity binding.

| Method/path | Request fields | Success |
| --- | --- | --- |
| POST `/api/v1/phone-verifications` | `phoneNumber` | 200 `{ challengeId, expiresAtUtc, resendAvailableAtUtc }` |
| POST `/api/v1/phone-verifications/{challengeId}/resend` | empty JSON object | 200 with the **new** challenge ID and timestamps; old challenge/proof invalidated |
| POST `/api/v1/phone-verifications/{challengeId}/verify` | `code` | 200 `{ verificationProof, expiresAtUtc }`; raw proof returned exactly once |
| POST `/api/v1/accounts` | `challengeId`, `verificationProof`, `fullName`, `password`, optional `emailAddress` | 201 `{ userAccountId, personIdentityId, createdAtUtc }`; no login/session/role/credentials |

All IDs are UUID v7. Purpose is server-owned `RegisterAccount`, not submitted. Register has **no phoneNumber field**: the verified challenge owns the phone. Account creation has no Location header because no authorized GET account resource exists in this sprint; 201 documents completion, not a fictitious read URL. There is no OTP-read HTTP endpoint or code in the issue response. No idempotency key/replayed security credential response: T17 security-specific one-use behavior applies.

## Input rules

- Phone input bounded to 32 UTF-16 code units before normalization. Trim surrounding whitespace, then accept ASCII digits only in `01[0125]xxxxxxxx`, `+201[0125]xxxxxxxx`, or `00201[0125]xxxxxxxx`. Normalize to `+201[0125]xxxxxxxx`. No spaces/dashes inside, non-Egyptian numbers or Unicode digit conversion.
- Full name: trim, NFC normalize, 2–200 UTF-16 units, reject control characters; this is a display name, not legal-identity evidence. No Latin-only rule.
- Email: omitted/null means none; when supplied, trim and lowercase invariantly; 3–254 ASCII characters, one `@`, local part ≤64, no whitespace/control/display name, valid DNS labels with a dotted domain. Normalized value is unique even while unverified. Empty supplied email is invalid.
- Password: 12–128 UTF-16 units, reject control characters; no trimming/normalizing/truncating, no arbitrary uppercase/symbol composition rule. PasswordHasher Identity V3, PBKDF2 iteration count 210,000 initial reviewed baseline; no password storage or logs beyond the resulting hash.
- OTP: exactly six ASCII digits. Proof: canonical unpadded Base64url of 32 random bytes (43 characters), no other representation. Challenge ID must be a nonempty UUID.
- Request errors use stable safe descriptions/field issues, never echo submitted values. Application paths exclude `body`; API adds that prefix by an explicit allowlist.

## Central security settings

| Setting | Initial value | Allowed configuration bound |
| --- | ---: | --- |
| OTP digits | 6 | fixed 6 for this contract |
| Code lifetime seconds | 300 | 60–600 |
| Proof lifetime seconds | 300 | 60–600 |
| Challenge maximum failed attempts | 5 | 1–10 |
| Minimum resend interval seconds | 60 | 60–300, ≤ code lifetime |
| Persistent rolling window seconds | 900 | 900–3600 |
| Exact target issue permits/window | 3 | 1–3 |
| Exact target verification permits/window | 10 | 1–10 |
| Password min/max and hasher iterations | 12/128; 210,000 | minimum 12–32; maximum 64–128 and ≥ min; iterations 210,000–1,000,000 |
| API bucket count | 4096 | 64–4096 |
| AnonymousIngress IP | 120/minute | 1–120 |
| OtpIssue IP | 20/900 seconds | 1–20 |
| OtpVerify IP | 30/minute | 1–30 |

API budgets use framework sliding windows, four segments, zero queue, separate named policies/signals and keyed **bounded bucket** partitions. RemoteIpAddress only; no forwarded-header trust configured. Missing IP uses one bounded fallback. Local single-instance process budgets reset on restart; durable exact target issue/verification windows, attempts and invalidation do not. A separate rate partition key (≥32 random bytes) never reuses OTP keys. Runtime options validate all bounds and cross-field constraints before startup, without exposing rejected values.

Target rolling state belongs to IdentityAccess. Issue/resend/verify serialize mutations for the normalized target using a transaction-scoped PostgreSQL lock, then consume persistent budgets. Rate state uses a purpose-independent keyed target digest. Target limits survive challenge replacement, provider failures and restart. A module-owned `rate_key_binding` fingerprint prevents silently replacing the digest key and resetting durable budgets; startup fails on a mismatch. Key rotation requires an explicit reviewed transition, not deleting active quota state. Retention cleanup removes expired challenges/counters after their security window, within owning schema; no arbitrary unbounded in-memory target registry.

OTP: CSPRNG unbiased integer generation; HMAC-SHA256 with unambiguous challenge/purpose/phone/code binding, independent versioned ≥32-byte key ring, constant-time equality. Proof: 32 random bytes, SHA-256 over bound challenge/purpose/phone/proof; no human-code hash used for proof. Domain reads injected UTC once per operation. `now >= expiry` is expired. Verified state is single-use; resend invalidates a verified unused proof too.

## Failure and delivery semantics

| Condition | Public status/code |
| --- | --- |
| Malformed shape/invalid fields | 400 `Validation.Failed`, bounded safe field issues when applicable |
| Unsupported media / too large / unacceptable representation | 415 / 413 / 406 with T32 transport codes |
| Unknown, expired, invalidated, exhausted or already-verified challenge; bad OTP/proof | 422 `IdentityAccess.VerificationRejected`, identical safe detail, no remaining attempts/state |
| Duplicate normalized phone/email or unavailable account creation | 409 `IdentityAccess.RegistrationRejected`, generic detail without field/existing-account disclosure |
| Any API or persistent security budget rejection | 429 `Infrastructure.RateLimitExceeded`; Retry-After only from a known limiter/window wait |
| Confirmed operational delivery unavailability | 503 `Infrastructure.DeliveryUnavailable` |
| Known lock contention / DB timeout with confirmed rollback | 503 `Infrastructure.Busy` / `Infrastructure.Timeout` |
| Unknown exception / uncertain commit outcome on this non-idempotent flow | 500 `General.UnexpectedError`; no claim of rollback/success and no automatic write retry |

The writer uses the fixed T32 category/type/title registry. All failures omit phone/email/OTP/password/proof/hash/key and provider/SQL diagnostics. Unregistered error codes/categories fail closed to sanitized 500. No WWW-Authenticate is invented for these anonymous commands.

Issue does not look up account existence: existing and new numbers have the same issue/delivery path and response. Account duplication is checked only after a valid proof demonstrates control; generic duplicate detail avoids revealing which contact conflicted. Verify unknown IDs consume IP protection; known target also consumes persistent verification budgets.

Issue first commits the reserved budget, invalidation and hashed active challenge, then delivers. Delivery is never attempted before successful commit. Failure after reservation keeps budgets consumed, invalidates the undelivered challenge when possible, and reports operational failure. It does not revive an old code or blindly retry. An uncertain commit never triggers delivery/re-execution. Verification could race delivery but can succeed only with the real code; failed/exhausted/invalidated states cannot produce a proof. After delivery, recheck current challenge and discard a superseded message. A response lost after a successful commit does not grant proof replay.

Registration locks authoritative challenge/target and checks proof, creates distinct account/person UUIDs and password hash, and consumes proof in **one database transaction**. Unique phone/email/person constraints arbitrate distinct concurrent requests. Validation/confirmed rollback does not consume proof. A lost/unknown commit is an infrastructure failure; client must not assume repeating a credential will return the original success. No automatic retry policy.

## Development mailbox and test adapter

Explicit user choice: `.local/otp/<challengeId>.json` under the reviewed repository root, local Development + loopback only. Directory 0700/file 0600, owned by the process user, excluded from Git. Check every existing path component and reject symbolic links; create secret files with restrictive permissions **at creation** and exclusive writes. One developer host owns the mailbox; a second process must fail instead of clearing another host's messages.

Message contains `{ challengeId, code, expiresAtUtc }`, no phone/password/proof. It is a temporary delivery message, not credential persistence; database retains HMAC only. Files are removed on invalidation, successful verification, expiry, and graceful host stop; startup removes stale owned messages after exclusive ownership. Periodic expiry cleanup and fail-closed startup handle leftovers from crashes. OS user is the trusted development recipient; this is not a protection against that user or a machine administrator.

Testing uses an in-memory sender installed solely by test composition. Native Testing has no real sender and cannot silently fall back to mailbox/SMS. No fixed OTP, bypass endpoint, SMS subscription or production helper is introduced.

## Threat review and required evidence

| Threat | Control/evidence |
| --- | --- |
| Brute force / resend resets / restart | attempts + persistent target budgets; IP sliding limiter; expiry/lock tests, budgets across challenges/restarts |
| OTP/proof reuse or cross-target swap | bound hash, state lock, constant-time comparison; concurrent verify/registration and wrong binding tests |
| Duplicate identities / orphan person | actual PG unique/FK/checks and one transaction; separate-connection races + rollback/final rows |
| Account enumeration | same issue behavior for existing/new phone; generic verified registration conflict; no remaining-attempt disclosures |
| Credential or PII disclosure | allowlisted DTOs/writer; no raw logging; synthetic sentinel response/log/report scans |
| Local delivery exposure | env/loopback gates, owner permissions, symlink rejection, exclusive ownership, TTL/lifecycle cleanup tests |
| Unexpected data/privilege escalation | strict request schema, normalized fields, internal domain mutation, restricted runtime role, no DDL |
| Infrastructure failure / commit ambiguity | reserve before deliver; fail closed/no retries; controlled sender/transaction fault tests |

Production prerequisites remain explicit: actual SMS vendor and quota/delivery contract, coordinated multi-instance abuse protection, reviewed numeric policy and hosting/forwarding/secret delivery T39. None are claimed satisfied by local tests.
