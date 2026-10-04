# S03 Authentication Contract

Approved scope: S03. Implementation authorized on 2026-10-04. This contract defines the first-party Development/Testing browser protocol; each endpoint becomes available with its owning task. It does not describe Production readiness, email login, MFA or institution access.

## Routes, ownership and idempotency

| Method / route | Classification | Credential | Success | Idempotency |
| --- | --- | --- | --- | --- |
| POST /api/v1/auth/login | AnonymousSecurity | Phone and password body | 200 access DTO + cookie | SecuritySpecific |
| POST /api/v1/auth/refresh | AnonymousSecurity | Refresh cookie | 200 access DTO + replacement cookie | SecuritySpecific |
| POST /api/v1/auth/logout | AnonymousSecurity | Refresh cookie; access JWT optional | 204 | SemanticallyIdempotent |
| POST /api/v1/auth/logout-all | AccountSelf | Bearer | 204 | SemanticallyIdempotent |
| GET /api/v1/accounts/me | AccountSelf | Bearer | 200 current-account DTO | NotRequiredRead |
| GET /api/v1/auth/sessions | AccountSelf | Bearer | 200 page envelope | NotRequiredRead |
| POST /api/v1/auth/sessions/{sessionId}/revoke | AccountSelf | Bearer | 204 | SemanticallyIdempotent |

A missing/non-owned target session is hidden with the same 404. An already-revoked owned target is a no-op. Authentication still applies to repeated requests; a revoked current bearer cannot be used to authenticate subsequent commands. Routes never accept another account's ID as authority.

## Browser transport

Development origin/issuer: `https://localhost:5101`. Audience: `educenteros-api`. Kestrel binds loopback; the existing trusted OS .NET development certificate supplies TLS. HTTP loopback 5100 remains available only for operational diagnostics and existing non-cookie registration flows; authentication commands reject HTTP rather than redirecting credential bodies. Browser/Swagger acceptance uses HTTPS exclusively.

All POST authentication/session commands require exactly one matching Origin and `X-EduCenterOS-Auth: 1`, JSON content type, and HTTPS. Origin is the exact configured scheme/host/port, not a prefix/suffix match; absent/null/multiple/mismatched origins fail 403 before effects. No wildcard credentialed CORS. Allowed methods: GET/POST; allowed headers: Authorization, Content-Type, X-EduCenterOS-Auth. CORS preflight never authenticates or mutates state. Queries on commands are rejected.

Refresh cookie:

```text
__Secure-educenteros-refresh
Path=/api/v1/auth
Domain omitted
Secure=true
HttpOnly=true
SameSite=Strict
Expires and Max-Age <= refresh expiration
```

Cookie deletion uses the same name/path/domain/Secure/SameSite policy. A refresh cookie alone does not authenticate a Business API. Login/refresh/logout-cookie handlers do not consume optional Bearer input as authority. Login CSRF has the same origin/header protection as refresh/logout.

Access JWT is returned in JSON and kept in browser memory only. Never persist authorization in Swagger or browser storage. Refresh is never returned in JSON or accepted from body/query/header alternatives. Disable credential request/response logging and saved HTTP-client history. Sensitive successes and failures use `Cache-Control: no-store`.

## Bodies and responses

Login body has exactly `phoneNumber` and `password`. Phone uses S02 Egyptian normalization. Password is required, 1–128 UTF-16 units without control characters; login does not enforce the current registration minimum on an existing password. Bound malformed input returns 400 without submitted values. Unknown mutation fields, duplicate/case-colliding keys and non-object JSON fail. Body maximum is 16384 bytes; UTF-8 JSON only. Refresh/logout/revoke commands accept an empty object only.

Access DTO:

```json
{"accessToken":"<memory-only>","tokenType":"Bearer","expiresAtUtc":"<UTC>"}
```

Current account DTO: userAccountId, personIdentityId, fullName, createdAtUtc. No password/hash, security counters, keys, phone/email or institution permissions.

Session DTO: sessionId, createdAtUtc, authenticatedAtUtc, lastSeenAtUtc, idleExpiresAtUtc, absoluteExpiresAtUtc, revokedAtUtc, isCurrent. No raw IP/User-Agent/credential/hash. Sessions include expired/revoked history; this sprint introduces no purge of security records.

Session pagination: page integer >=1, default1; pageSize integer1–100, default20. Unknown/duplicate/malformed query fields fail400. Sort: createdAtUtc DESC, id DESC. Envelope follows T31 with items and pagination {type:"page",page,pageSize,totalCount,totalPages}. Page drift during concurrent writes is documented; no cursor/Data Protection dependency.

## Failures and headers

Errors use the existing T32 ProblemDetails writer, correlationId and safe codes. No SQL/provider details, submitted password, token, PEM, hashes or account lifecycle disclosure.

| Condition | HTTP / code |
| --- | --- |
| Invalid bounded field/shape | 400 / Validation.Failed or existing transport code |
| Login unknown/wrong/unverified/locked/suspended/closed | 401 / IdentityAccess.AuthenticationRejected |
| Invalid/expired/revoked/missing credential or JWT | 401 / IdentityAccess.AuthenticationRejected |
| Browser transport/Origin/header denial | 403 / IdentityAccess.BrowserRequestRejected |
| Non-owned/missing target session | 404 / IdentityAccess.SessionNotFound |
| Identifier/source throttle | 429 / IdentityAccess.AuthenticationThrottled |
| Oversized/unsupported body/content | Existing 413/415 mappings |
| PostgreSQL outage or unclassified commit ambiguity | Existing safe 500/503 infrastructure mapping |

Every 401 includes `WWW-Authenticate: Bearer` without error_description; the challenge covers the closed first-party authentication contract. Public failures use the same account-neutral code/body; input errors and source-wide 429 are independent of account existence. Retry-After is supplied only for rate policy deadlines, never a revealed account-lockout timestamp.

## Configuration and numeric Development baselines

Reviewed non-secret policy lives in infra/authentication-policy.json. Tests use the same protocol with a distinct synthetic issuer/origin and runtime-generated RSA keys. These are technical Development baselines, not final Production tuning.

| Field | Value |
| --- | --- |
| accessLifetimeSeconds | 600 |
| idleTimeoutSeconds | 2592000 (30 days) |
| absoluteLifetimeSeconds | 7776000 (90 days) |
| clockSkewSeconds | 5 |
| lockoutAttempts | 5 |
| lockoutSeconds | 300 |
| loginWindowSeconds | 900 |
| loginIdentifierPermits | 10 |
| sourceWindowSeconds | 60 |
| sourcePermits | 60 |
| refreshSourcePermits | 30 |
| maximumValidationKeys | 4 (validator bound, not policy field) |

Identifier rolling budgets are persisted as HMAC-partitioned targets, including unknown accounts; account success does not erase the shared budget. Counts are serialized by an owning target row lock and trimmed only outside the active window. Source limits use the actual loopback connection IP, no untrusted forwarded headers, with a fixed finite 256-bucket keyed partition mapping; collisions conservatively share budgets. Identifier throttling can precede the account lockout for repeated attempts in an already-used window. Cleanup of stale identifier-only budgets cannot delete entries within the rolling window.

Configuration rejects missing/unknown/duplicate fields, invalid URLs/ports, non-HTTPS origins, wildcard/null/credentials/query/fragment origins, invalid ranges and inconsistent lifetimes. Domain receives values, not IConfiguration/IOptions. Runtime snapshot moves from schema3 to schema4 when auth becomes required in T03, adds authenticationPolicy, and extends the secret allowlist with private PEM/current kid/public PEM map. Maximum snapshot UTF-8 size is 65536 bytes for at most four RSA public keys. Old/partial schema fails, with an explicit simultaneous bootstrap/test-fixture upgrade.

Development Infisical scope remains /backend-api/supabase-development. New canonical keys: IdentityAccess__Jwt__PrivateKeyPem, IdentityAccess__Jwt__CurrentKeyId, IdentityAccess__Jwt__ValidationPublicKeys__<kid>. No copied private key in repo, command arguments, build or CI. Use a purpose-separated 2048-bit-or-stronger RSA key; current public member must match private material. Existing OTP/partition/DB bundle remains intact. Bootstrap validation permits only complete reviewed auth extensions; no automatic key overwrite or fallback.

JWT public key rotation uses prepublish → activate → retain old validation key until last issued expiration + explicit skew → retire. IDs are immutable lowercase ASCII names; unknown kid fails. The provider supports overlapping known public keys but never accepts token-provided URLs. Local OS TLS certificate is distinct from JWT signing material.

## JWT/server validation

Framework-backed JWT issuance/validation: Microsoft.AspNetCore.Authentication.JwtBearer aligned with pinned ASP.NET Core 10.0.7; transitively resolved Microsoft.IdentityModel libraries tracked in lock files. No handwritten JWT signing/validation. Use RS256 only, typ=educenteros-access+jwt, known kid, claims allowlist iss/aud/sub/sid/jti/iat/exp/sv; no nbf/PII/account status/roles/institutions/assurance claims. Claims are unique and properly typed; Guid.Empty, malformed UUIDs, missing/invalid sv/jti/iat/exp and exp<=iat fail.

MapInboundClaims=false, SaveToken=false, IncludeErrorDetails=false. Require signature, expiration, matching issuer/audience; no remote authority/discovery. Server validates Active account, session ownership/non-revocation/deadlines and JWT.sv==Session.SecurityVersionAtAuthentication==Account.SecurityVersion every protected request. No authentication-state caching. DB failure propagates as safe infrastructure failure, not authenticated access or invented bad-password result.

Access expiration is bounded by min(now+600s, session idle, session absolute). JWT expiry uses seconds with explicit skew; server session deadlines use inclusive now>=deadline with no skew extension. GET requests never slide session lifetime; successful Refresh alone updates LastSeen/idle, clipped to absolute lifetime. Refresh rejects before consumption when the absolute deadline cannot support a positive whole-second cookie Max-Age; no zero-lifetime access/cookie grant is committed.

## Atomic state and races

Account → Sessions ordered by ID → Credentials ordered by ID is the lock order whenever needed. Non-locking candidate lookup is followed by authoritative reload. Password verification occurs outside the transaction; the locked account's hash/status/lockout/SecurityVersion are checked before trusting the result. A changed snapshot fails safely rather than silently signing with stale evidence. Failed increments use an account lock; rehash changes neither PasswordChangedAtUtc nor SecurityVersion.

Login session/refresh/reset/rehash commit atomically. No credentials reach the client before Commit; no blind retry near ambiguous Commit. Refresh is CSPRNG32 bytes encoded Base64Url; only SHA256 digest persists. Rotation consumes A, creates B, links lineage and updates activity in one transaction. Reuse revokes the entire session durably, without grace or cached secret replay. Real successful Commit with lost response then retry A causes reuse revocation and new Login requirement; browser requires single-flight Refresh.

Logout current uses a resolvable refresh record, even if Access expired; absent/unresolvable cookie is an external no-op with deletion attempt. Database resolution failure is not a no-op. LogoutAll requires live Bearer, locks account then its sessions, revokes sessions existing at that boundary and does not increment SecurityVersion. A later Login creates a new session. Repeated revocation is terminal/no-op, never revival.

All cryptographic/provider/database failures are fail-closed. Tests cover true PostgreSQL overlap, final state/lineage, rollback and actual Commit then lost response. Deferred suspension/security-reset flows are exercised through owned test setup using their lock/security protocol, not public backdoors.

## References

- T12/T13/T14/T15/T16/T17/T31/T32/T33/T35/T36/T40/T41/T42 and docs/sprints/S03.md.
- [ASP.NET Core JWT bearer validation](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0): framework validation mechanics; this project keeps its approved closed first-party T13 protocol rather than introducing OAuth/OIDC scope.
- [ASP.NET Core HTTPS and development certificates](https://learn.microsoft.com/en-us/aspnet/core/security/enforcing-ssl?view=aspnetcore-10.0).
- [ASP.NET Core CORS](https://learn.microsoft.com/en-us/aspnet/core/security/cors?view=aspnetcore-10.0).
