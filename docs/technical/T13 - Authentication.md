# T13 - Authentication

## القرار

EduCenterOS يستخدم في Core V1:

```text
First-party Authentication
+
Server-side UserSession
+
Short-lived Signed JWT Access Token
+
Opaque Rotating Refresh Credential
+
Immediate Session Revocation
+
Refresh Reuse Detection
+
TOTP-based MFA for Sensitive Access
+
Hashed Single-use Recovery Codes
+
Short-lived Purpose-bound Step-up Grants
```

ولا يستخدم حاليًا:

```text
JWT-only Authentication
Cookie-only Business API Authentication
OAuth/OIDC Authorization Server
OpenIddict
External Identity Providers
BFF
SMS OTP as the permanent second factor for sensitive roles
Global session-wide Step-up that unlocks every sensitive operation
```

لو ظهر مستقبلًا SSO أو Third-party Clients أو Delegated Authorization، يعاد تقييم OAuth/OIDC بقرار مستقل.

---

# 1. Authentication vs Authorization

T13 تجيب على:

```text
Who is this actor?
Is this session valid?
What authentication assurance currently exists?
Has the required recent authentication / step-up proof been satisfied?
```

ولا تجيب على:

```text
Which institution?
Which role?
Which branch?
Which permissions?
Can this resource operation be performed?
```

دي مسؤولية T14 والـBusiness Modules.

---

# 2. Login

الـLogin الأساسية:

```text
Phone / Verified Email
+
Password
↓
Authenticate Account
↓
MFA challenge when an active method exists
↓
Create UserSession only after required factors succeed
↓
Issue Access JWT
+
Refresh Credential
```

الموبايل يستخدم نفس Normalization المعتمدة في T12.

Email لا تستخدم Login إلا لو موثقة.

---

# 3. Access Token

الـAccess Token:

```text
JWT
Signed
Bearer
Short-lived
```

وترسل:

```http
Authorization: Bearer <access-token>
```

الـJWT Signed وليست Encrypted.

لذلك لا نضع فيها بيانات شخصية أو Authorization state غير لازمة.

---

# 4. JWT Algorithm

الـbaseline:

```text
RS256
```

مع:

```text
kid
typ = educenteros-access+jwt
```

Validation تسمح صراحة بـ:

```text
alg = RS256 only
typ = educenteros-access+jwt exactly
kid = known locally configured validation key
```

ولا يسمح للـToken باختيار Algorithm أو Key source مختلفة.

لا نستخدم remote key URLs من الـToken مثل:

```text
jku
x5u
```

في V1.

أي `kid` غير معروف يفشل الطلب، ولا يعمل النظام fallback إلى مفتاح افتراضي أو remote discovery غير موثوق.

---

# 5. JWT Claims

الـAccess JWT تحتوي فقط على:

```text
iss
aud
sub
sid
jti
iat
exp
sv
```

المعاني:

```text
sub
→ UserAccountId

sid
→ UserSessionId

sv
→ SecurityVersion snapshot
```

ولا تحتوي:

```text
Phone
Email
FullName
AccountStatus

InstitutionId
MembershipId
Role
BranchScope
Capabilities
Permissions

StudentProfile
GuardianRelationship
OnboardingIntent
```

ولا يوجد `AccountMode` أصلًا.

---

# 6. Authorization State لا تدخل JWT

JWT تثبت فقط:

```text
Authenticated account
+
Session
+
Security epoch
```

أما:

```text
InstitutionMembership
Role
Scope
Capabilities
Eligibility
Approvals
```

فتتحقق Server-side.

---

# 7. Access Lifetime

الـbaseline التقنية الحالية:

```text
AccessTokenLifetime = 10 minutes
```

وتكون قابلة للConfiguration.

لكن الـToken لا تتجاوز عمر Session الفعلي:

```text
AccessExpiresAtUtc =
MIN(
    nowUtc + AccessTokenLifetime,
    SessionEffectiveExpiration
)
```

---

# 8. JWT Validation

كل Protected Request تمر بمرحلتين:

```text
1. Cryptographic JWT Validation
2. Server-side Session + Account Validation
```

Valid signature وحدها لا تكفي.

بعد قراءة:

```text
sub
sid
sv
```

نتحقق من:

```text
Session exists
Session belongs to UserAccount
Session not revoked
Session not expired
Account is Active
Security versions agree
```

كذلك Claims التالية كلها Required وليست اختيارية:

```text
iss
aud
sub
sid
jti
iat
exp
sv
```

وتفشل المصادقة لو:

```text
Claim missing
Claim malformed
Identifier is Guid.Empty
exp <= iat
Token type / algorithm / issuer / audience mismatch
Unknown kid
```

لا نصدر `nbf` في Core V1. لو أضيفت لاحقًا تصبح Required وتدخل validation profile نفسها.

---

# 9. SecurityVersion

كل Session تخزن:

```text
SecurityVersionAtAuthentication
```

كـSnapshot وقت Full Login.

ويجب أن يتحقق:

```text
JWT.sv
==
Session.SecurityVersionAtAuthentication
==
UserAccount.SecurityVersion
```

وإلا Authentication تفشل.

---

# 10. Security Reset

الـAccount-wide transitions التالية إلزاميًا تعمل داخل Boundary ذرية واحدة:

```text
Password change / reset     → SecurityVersion++ + revoke all sessions
Account recovery            → SecurityVersion++ + revoke all sessions
Phone credential change     → SecurityVersion++ + revoke all sessions
Account security reset      → SecurityVersion++ + revoke all sessions
MFA disable / replacement   → SecurityVersion++ + revoke all sessions
Account suspension          → SecurityVersion++ + revoke all sessions
Account closure             → SecurityVersion++ + revoke all sessions
```

أما:

```text
Logout all                  → revoke all sessions only
Membership / role change    → no SecurityVersion increment by default
```

لأن Institution authorization يعاد تقييمها Server-side في كل عملية لاحقة.

ولا يوجد أي `AccountMode transition` ضمن هذا النموذج.

Institution membership/role changes لا تغير `SecurityVersion` افتراضيًا.

---

# 11. UserSession

الشكل المفاهيمي:

```text
UserSession

Id
UserAccountId

SecurityVersionAtAuthentication

CreatedAtUtc
AuthenticatedAtUtc
LastPrimaryAuthenticatedAtUtc
LastSeenAtUtc

IdleExpiresAtUtc
AbsoluteExpiresAtUtc

RevokedAtUtc?
RevocationReason?

AssuranceLevel
MfaSatisfiedAtUtc?

ClientType?
DeviceDisplayName?
UserAgentSummary?
CreatedFromIp?
LastSeenFromIp?

Version
```

Device metadata السابقة تكون Coarse display/security signals لإدارة الجلسات والإبلاغ عن نشاط غير معروف.

القواعد:

- لا تعتبر Proof of identity.
- لا تمنح Authorization.
- لا نبني Fingerprinting خفي أو دائم في Core V1.
- تعامل IP/User-Agent كبيانات أمنية/شخصية في Logs وRetention.

---

# 12. Authentication Assurance

نستخدم مفهوم:

```text
Authentication Assurance
```

بدل التعامل مع Authentication كـtrue/false فقط.

الحد الأدنى المفاهيمي:

```text
PrimaryAuthenticated
MultiFactorAuthenticated
```

في Core V1:

```text
Password-only completed Login
→ PrimaryAuthenticated

Password + TOTP/Recovery completed Login
→ MultiFactorAuthenticated
```

والـStep-up يمكن أن تنتج Evidence أعلى/أحدث للعملية المحددة بدون منح Permission.

---

# 13. IAuthenticationContext

لما تحتاج T14 إثباتات Authentication لا نضخم `ICurrentActor`.

نستخدم Contract صغيرة مثل:

```csharp
public interface IAuthenticationContext
{
    DateTimeOffset InitialAuthenticatedAtUtc { get; }

    DateTimeOffset LastPrimaryAuthenticatedAtUtc { get; }

    AuthenticationAssuranceLevel AssuranceLevel { get; }

    DateTimeOffset? MfaSatisfiedAtUtc { get; }

    bool HasActiveMfaMethod { get; }
}
```

الـAuthentication تملك الـevidence.

T14 تقرر:

> ما مستوى الـAssurance المطلوب للعملية؟

وبالتالي:

```text
Authentication evidence
≠
Authorization permission
```

---

# 14. Recent Authentication

`Recent Authentication` ليست Permission ولا Role.

هي Requirement أمنية يتم تقييمها من Authentication evidence.

مثال:

```text
Sensitive operation
↓
Permission satisfied
+
Recent Authentication required
+
MFA required if applicable
```

تفاصيل الـStep-up policy النهائية تتبع T14.

`Recent Authentication` تقاس من Evidence المناسبة للعملية، وليس دائمًا من وقت إنشاء Session:

```text
Password re-authentication
→ LastPrimaryAuthenticatedAtUtc

MFA challenge
→ MfaSatisfiedAtUtc

Purpose-bound sensitive operation
→ valid StepUpGrant for the exact purpose/context
```

## 14.1 Minimal MFA Baseline

Core V1 تنفذ MFA حقيقية للأدوار والصلاحيات الحساسة المحددة في T14.

الـbaseline:

```text
Authenticator TOTP
+
Single-use Recovery Codes
```

SMS OTP تظل مناسبة للتحقق من ملكية الهاتف وبعض recovery/step-up flows المحددة، لكنها ليست العامل الثاني الدائم للأدوار المالية والإدارية الحساسة.

MFA enrollment تحتاج:

```text
Valid active session
+
Recent primary authentication
+
Generate secret using CSPRNG
+
Display secret once through protected response
+
Verify one TOTP before activation
+
Audit security event
```

تعطيل أو استبدال MFA يحتاج Step-up مناسب، ويجب ألا يترك حسابًا لديه Access حساسة في حالة تخالف Requirement الإلزامية.

## 14.2 TOTP Rules

نستخدم Implementation/Library مجربة ومتوافقة مع RFC 6238، ولا نكتب TOTP cryptography يدويًا.

الـbaseline القابلة للConfiguration:

```text
Time step = 30 seconds
Accepted drift = current step ± 1 step
Digits = 6
Algorithm = SHA-1 for broad authenticator interoperability
```

استخدام SHA-1 هنا داخل معيار TOTP/HMAC للتوافق لا يعني استخدام SHA-1 لتخزين Password أو Token عامة.

القواعد:

```text
Secret generated with CSPRNG
Secret encrypted at rest using managed application key
Secret never logged
Enrollment pending until first successful verification
Small centrally configured clock-drift window
Attempt limits and rate limits
Replay of the same accepted time-step is rejected per account/session
Constant-time comparison where applicable
```

تغيير TOTP parameters أو encryption/key strategy يحتاج Security review موثقة.

## 14.3 Recovery Codes

Recovery Codes:

```text
High entropy
Displayed once
Stored only as one-way hashes
Single-use
Individually revocable/consumable
Regeneration invalidates every older unused code
```

الـbaseline:

```text
10 codes per generation
At least 128 random bits per code before display encoding
HMAC-SHA-256 storage with server key + key version
```

الـHMAC تمنع تجربة الأكواد Offline عند تسرب قاعدة البيانات، والمفتاح لا يخزن معها.

استخدام Recovery Code:

```text
raises MFA evidence for the intended flow
+
is consumed atomically
+
creates an audit/security event
+
may trigger session review or notification
```

## 14.4 Purpose-bound StepUpGrant

لا نرفع Session كلها بصورة عامة لكل العمليات الحساسة.

بعد نجاح Challenge إضافية ننشئ Server-side proof قصيرة العمر:

```text
StepUpGrant

Id
UserSessionId
Purpose
TargetResourceId?
AchievedAssuranceLevel
IssuedAtUtc
ExpiresAtUtc
ConsumedAtUtc?
Version
```

الـbaseline:

```text
Default StepUpGrant lifetime = 5 minutes
Recent primary authentication window = 10 minutes
High-risk grants = single-use
```

القيم مركزية ويمكن لتصنيف Risk أشد أن يطلب مدة أقصر، ولا توجد Policy تسمح للـGrant بتجاوز Session expiration.

القواعد:

- `Purpose` تأتي من Use Case/Policy ثابتة وليست generic string يختارها Client.
- Grant مرتبطة بالـSession الحالية، ولا تنتقل إلى Session أخرى.
- Target binding تستخدم عندما تكون العملية مرتبطة Resource محددة.
- العمليات عالية الخطورة تستهلك Grant أحادية الاستخدام داخل IdentityAccess ثم تصدر `OperationAuthorizationTicket` مقيدة للعملية؛ لا نفتح Cross-module transaction مع الـBusiness Module.
- Recent-auth window العامة، إن سمحت بها Policy، تظل قصيرة ولا تتجاوز Session expiration.
- Expired/consumed/revoked-session grant تفشل Fail Closed.
- Step-up لا تمنح Role أو Permission أو Institution access.

T13 تملك إنشاء/تخزين/التحقق من Authentication proof، بينما T14 تملك تحديد أي Permission/Operation تتطلبها ونوعها.

## 14.5 OperationAuthorizationTicket

بعد استهلاك `StepUpGrant` للعملية عالية الخطورة، تنشئ IdentityAccess تذكرة handoff قصيرة العمر:

```text
OperationAuthorizationTicket

Id
StepUpGrantId
TicketHandleHash
HashKeyVersion
UserAccountId
UserSessionId
TargetModule
OperationCode
TargetResourceId?
OperationId
AchievedAssuranceLevel
IssuedAtUtc
ExpiresAtUtc
RevokedAtUtc?
Version
```

الـbaseline:

```text
Lifetime = 2 minutes
Raw handle entropy >= 256 random bits
Storage = HMAC-SHA-256 hash with managed server key + key version
```

القواعد:

- استهلاك Grant وإنشاء Ticket يحدثان في IdentityAccess transaction واحدة.
- Raw handle تظهر مرة واحدة في protected `no-store` response، ولا تحفظ أو تسجل أو توضع في URL.
- Ticket مرتبطة بالـSession وTarget Module وOperationCode وTarget Resource عند وجودها و`OperationId` التي أنشأها الـClient/Server للنية نفسها.
- Expiry لا تتجاوز Expiry الخاصة بالـGrant أو الـSession المالكة.
- IdentityAccess تتحقق Server-side من Hash والـbindings وحالة الحساب/Session قبل أن تبدأ Target Module عمليتها المحلية.
- Target Module تحفظ `TicketId + OperationId` مع mutation داخل transaction واحدة وتفرض uniqueness؛ هذه هي نقطة single execution للـBusiness effect.
- إعادة المحاولة مسموحة فقط لنفس `OperationId` والbindings قبل Expiry. تغيير أي binding يفشل Fail Closed.
- فشل Target transaction لا يعيد Grant المستهلكة للحياة؛ يمكن إعادة نفس intent بالـTicket القائمة قبل انتهائها، ثم يلزم Step-up جديدة.
- فقد الاستجابة بعد Commit يحل بنتيجة Idempotency لنفس `OperationId` حسب T15/T17.

الـTicket Authentication evidence وليست Role أو Permission أو Approval business record.

---

# 15. Session Lifetimes

الـbaseline التقنية الحالية:

```text
Standard Idle Timeout = 30 days
Absolute Lifetime     = 90 days
```

وتدار مركزيًا من Configuration.

هذه مدة Standard account session، وليست مدة سماح للعمليات الإدارية الحساسة. الوصول الحساس يعتمد على MFA وStepUpGrant قصيرة العمر وفق T14، لذلك لا يرث كل عمر Session الطويل.

لو أضيف مستقبلًا Dedicated Administrative Session Profile، يجب أن تكون Idle/Absolute lifetimes أقصر بقيم Configuration مستقلة، وألا تستنتج IdentityAccess الدور من JWT أو Client claim.

الـSession تعتبر منتهية إذا:

```text
nowUtc >= IdleExpiresAtUtc
OR
nowUtc >= AbsoluteExpiresAtUtc
```

القيم ليست Business constants داخل الـDomain.

---

# 16. Session Activity

لا نعمل write لـ`LastSeenAtUtc` مع كل API request.

Successful Refresh تعتبر Activity:

```text
LastSeenAtUtc = nowUtc
```

ويمكنها تحريك Idle deadline:

```text
IdleExpiresAtUtc =
MIN(
    nowUtc + IdleTimeout,
    AbsoluteExpiresAtUtc
)
```

لكن Absolute lifetime لا تتحرك.

---

# 17. Refresh Credential

Refresh Credential تكون:

```text
Opaque
High entropy
Machine-generated
Single-use after successful rotation
```

وليست JWT أو GUID.

الـbaseline:

```text
32 random bytes
```

باستخدام CSPRNG.

---

# 18. Refresh Storage

Raw Refresh Credential لا تخزن.

```text
Raw Refresh
↓
SHA-256
↓
TokenHash
```

والـDatabase تخزن الـHash فقط.

SHA-256 هنا مناسبة لأن الـRefresh نفسها Secret عشوائية عالية الـentropy، وليست Password بشرية.

---

# 19. RefreshTokenRecord

```text
RefreshTokenRecord

Id
UserSessionId

TokenHash

CreatedAtUtc
ExpiresAtUtc

ConsumedAtUtc?
RevokedAtUtc?

ReplacedByTokenId?

Version
```

`Id` تستخدم UUID v7 وليست Credential.

ويجب دائمًا:

```text
RefreshTokenRecord.ExpiresAtUtc
<=
MIN(Session.IdleExpiresAtUtc, Session.AbsoluteExpiresAtUtc)
```

الـRefresh validation تتحقق من صلاحية Record والـSession والـAccount معًا؛ صلاحية Record وحدها لا تكفي.

---

# 20. Refresh Rotation

Refresh Token واحدة تستخدم مرة واحدة.

```text
A
↓
Validate
↓
Consume A
↓
Create B
↓
A.ReplacedByTokenId = B.Id
↓
Commit
```

بعد النجاح:

```text
A ❌
B ✅
```

---

# 21. Rotation Transaction

العمليات التالية تحدث داخل Transaction واحدة:

```text
Validate current refresh
Consume old refresh
Create new refresh
Link old → new
Update session activity
Update idle deadline
Commit
```

ولا تصل Credential الجديدة للـClient قبل نجاح الـCommit.

بعد نجاح Commit فقط:

```text
Set replacement refresh cookie B
Issue access token bound to the same session
```

ولو فشل Commit لا ترسل `B` ولا Access Token قابلة للاستخدام.

---

# 22. Refresh Reuse Detection

لو Refresh Credential سبق استهلاكها وتم تقديمها مرة أخرى:

```text
Reuse detected
↓
Revoke UserSession
↓
All refresh credentials for session unusable
↓
All Access JWTs carrying same sid rejected
↓
Full Login required
```

في V1:

```text
No grace period
```

والـClient يجب أن تستخدم Single-flight refresh.

---

# 23. Lost Refresh Response

لو حصل:

```text
A → B
Commit
Response lost
```

ثم أعاد Client إرسال `A`:

```text
Reuse Detection
→ Session revoked
```

ده Tradeoff مقصود في V1.

لا نخزن Raw `B` بهدف replay.

---

# 24. Login Enumeration Protection

الـPublic Login failure لا تفرق بين:

```text
Unknown Account
Wrong Password
Unverified Email
Temporary Lock
Suspended
Closed
```

وعند Unknown Account نستخدم:

```text
Dummy Password Verification
```

لتقليل Timing-based account enumeration.

نفس شكل الاستجابة الخارجي وHTTP mapping يستخدم بقدر عملي لكل حالات الفشل العامة، مع عدم كشف lockout/account state في body أو timing بصورة مباشرة.

---

# 24.1 Login Abuse Protection

Account lockout ليست طبقة الحماية الوحيدة.

نطبق Rate limiting/abuse controls Server-side باستخدام أكثر من Signal:

```text
Normalized login identifier hash
Account when resolved
Source IP / network signal
Session or client signal when available
Rolling time windows
```

القواعد:

- Counters الخاصة بالـIP والIdentifier لا تختفي بمجرد استخدام Account غير موجودة.
- نجاح Login يمكن أن يصفر `AccessFailedCount` للحساب، لكنه لا يمسح Abuse windows العامة بصورة تسمح بالتحايل.
- نستخدم Progressive delay أو temporary throttling قبل الاعتماد على Lockout وحدها.
- Limits وقيم النوافذ والـdelays مركزية في Security Configuration.
- القرار الخارجي يظل Enumeration-safe.
- لا نثق في IP وحدها ولا نحول Device signal إلى Proof of identity.
- أحداث brute force/credential stuffing غير الطبيعية تدخل Security telemetry بدون تسجيل Raw password أو tokens.

---

# 25. Temporary Lockout

Temporary Lockout تستخدم:

```text
AccessFailedCount
LockoutEndUtc?
```

وليست `AccountStatus`.

Baseline الحالية:

```text
5 failed attempts
→ 5 minute lockout
```

والقيم Configuration مركزية.

التحديثات المتزامنة لـ`AccessFailedCount` و`LockoutEndUtc` يجب ألا تفقد محاولات فاشلة، وآلية الحماية الذرية يحسمها T16 وتغطيها PostgreSQL integration tests.

---

# 26. Password Rehash

لو Password verification ترجع:

```text
SuccessRehashNeeded
```

يتم تحديث `PasswordHash`.

لكن ذلك وحده لا يغير:

```text
PasswordChangedAtUtc
SecurityVersion
```

لأنه Transparent cryptographic maintenance وليس password change من المستخدم.

---

# 27. Successful Login

لو الحساب لا يملك MFA فعالة، الـflow:

```text
Resolve account
↓
Verify password
↓
Check account status / lockout
↓
Reset failed state
↓
Optional password rehash
↓
Create UserSession
↓
Generate Refresh Secret
↓
Store Refresh Hash
↓
Create RefreshTokenRecord
↓
Issue Access JWT
↓
Commit
↓
Return credentials
```

لا Credentials تصل للClient قبل نجاح Persistence.

لو الحساب يملك MFA فعالة، Password success لا تنشئ Session كاملة ولا Tokens بعد:

```text
Resolve account
↓
Verify password / status / lockout
↓
Create short-lived MfaLoginChallenge
↓
Commit primary-stage security state/challenge
↓
Return opaque challenge handle only
↓
Verify TOTP or Recovery Code with attempt/rate limits
↓
Consume challenge atomically
↓
Consume Recovery Code atomically when used
↓
Create UserSession with MultiFactorAuthenticated evidence
↓
Create RefreshTokenRecord
↓
Commit
↓
Return Access JWT + refresh cookie
```

`MfaLoginChallenge`:

- مرتبطة بـUserAccount وPurpose ثابتة ووقت انتهاء قصير.
- الـbaseline لعمرها 5 دقائق وتدار من Configuration.
- Handle = 256 random bits من CSPRNG، تنقل Base64Url، ويخزن SHA-256 hash فقط.
- Single-use ولا تتحول إلى Session لو انتهت أو استهلكت.
- لا تكشف خارجيًا هل Account موجودة أو هل MFA مفعلة قبل نجاح Password.
- نجاح العامل الأول لا يعتبر Authentication كاملة قابلة لاستخدام Business APIs.
- Reset failed state وoptional password rehash يمكن حفظهما مع إنشاء Challenge، لكن لا تنشأ Session/credentials قبل نجاح العامل الثاني.

لو UserAccount لا تملك MFA لكن T14 اكتشفت أنها تحاول Access تتطلب MFA إلزامية، تسمح Authentication بجلسة Standard محدودة، بينما Authorization ترجع `MfaEnrollmentRequired` ولا تمنح العملية الحساسة قبل Enrollment ناجحة.

---

# 28. Registration ≠ Login

نجاح `RegisterAccount` في T12 لا يعمل Auto-login.

```text
Registration
≠
Create Session
```

المستخدم يدخل من Login flow واضحة.

---

# 29. Browser Token Model

Core V1 في هذا القرار تعرف Browser first-party client فقط. أي Native Mobile/Desktop client يحتاج قرارًا مكملًا يحدد Secure OS storage وtoken transport؛ ممنوع نسخ Browser cookie model أو تخزين Refresh Credential في storage عادية تلقائيًا.

## Access Token

ترجع في JSON وتحفظ:

```text
Memory only
```

ولا تحفظ في:

```text
localStorage
sessionStorage
IndexedDB
```

## Refresh Credential

لا ترجع في JSON.

توضع في:

```text
HttpOnly Cookie
Secure
SameSite = Strict
Host-only
```

وبأضيق Path عملية ممكنة.

الـbaseline التنفيذية:

```text
Cookie name = __Secure-educenteros-refresh
Domain attribute = omitted       // Host-only
Path = exact shared auth path required by refresh/logout
HttpOnly = true
Secure = true
SameSite = Strict
Max-Age/Expires <= RefreshTokenRecord.ExpiresAtUtc
```

لا نستخدم `__Host-` لأن Core V1 تريد Path أضيق من `/`. لو تغيرت السياسة إلى `Path=/` يمكن تقييم `__Host-`.

Cookie Path ليست Security boundary؛ الحماية الحقيقية تأتي من سرية الـCredential وCSRF controls والتحقق Server-side.

T31 يجب أن يحافظ على Browser وAuthentication endpoints داخل same-site deployment compatible مع `SameSite=Strict`. لو اختير Cross-site deployment مستقبلًا، لا نخفف Cookie policy صامتًا؛ يحتاج Threat review وقرار CSRF/Cookie محدث.

---

# 30. Business API Authentication

الـBusiness APIs تظل:

```text
Authorization: Bearer
```

الـRefresh Cookie ليست Cookie Authentication عامة للتطبيق.

---

# 31. CSRF Protection

لأن Browser ترسل Refresh Cookie تلقائيًا، Refresh/Logout endpoints تستخدم دفاعات مثل:

```text
SameSite = Strict
Exact allowed Origin
Strict CORS
Required non-simple custom header
POST only
Reject simple content types
Reject missing/mismatched Origin on browser flows
```

وممنوع:

```text
AllowAnyOrigin + AllowCredentials
```

الـcustom header يجب أن تكون Allow-listed صراحة في CORS، والـOrigin تقارن كـscheme + host + port كاملة بدون suffix matching.

`SameSite` طبقة إضافية وليست بديلًا عن Origin/CORS/header validation.

---

# 32. Sensitive Responses

Authentication responses الحساسة تستخدم:

```http
Cache-Control: no-store
```

---

# 33. Logout Current

تعني:

```text
Revoke current UserSession
```

والـsource of truth:

```text
Session.RevokedAtUtc
```

مش مجرد حذف Token من Browser.

Browser `POST logout-current` يمكنها resolve الجلسة من Refresh Credential الحالية مع CSRF controls، ولا تشترط Access JWT غير منتهية؛ انتهاء Access Token لا يجب أن يمنع إبطال Session. لو Cookie غائبة/غير صالحة تكون النتيجة الخارجية idempotent no-op مع محاولة حذفها.

لو Session already revoked:

```text
No-op success
```

في كل حالات Logout الخاصة بالBrowser، نحاول كذلك Expire/Delete refresh cookie بنفس:

```text
Name
Path
Domain policy
Secure/SameSite compatibility
```

لكن نجاح حذف Cookie من Client ليس Source of Truth؛ إبطال الـSession Server-side هو الأساس.

---

# 34. Revoke Specific Session

المستخدم يمكنه إبطال Session أخرى تخص نفس حسابه.

يجب إثبات:

```text
TargetSession.UserAccountId
==
CurrentActor.UserAccountId
```

SessionId نفسها ليست Authorization.

---

# 35. Logout All

```text
Revoke all current sessions
```

ولا تغير `SecurityVersion`.

الفرق:

```text
Logout All
→ revoke sessions only

Security Reset
→ SecurityVersion++
→ revoke sessions
```

Browser الحالي يحذف refresh cookie المحلية بعد نجاح/No-op logout-all. Cookies الموجودة على أجهزة أخرى تصبح غير قابلة للاستخدام بسبب Session revocation حتى لو ظلت محفوظة هناك.

---

# 36. Revoked Session Terminal

لو:

```text
RevokedAtUtc != null
```

لا يتم إعادة Session للحياة.

أي Authentication جديدة تنشئ Session جديدة.

---

# 37. ICurrentActor

تظل صغيرة:

```csharp
public interface ICurrentActor
{
    Guid UserAccountId { get; }

    Guid UserSessionId { get; }
}
```

ولا تحتوي:

```text
AccountStatus
InstitutionId
Role
BranchScope
Capabilities
StudentProfile
Authentication Assurance
```

---

# 38. HttpContext Boundary

`HttpContext` و`IHttpContextAccessor` يظلان داخل Infrastructure Adapter فقط.

ممنوع اعتمادهما داخل:

```text
Domain
Feature Handlers
```

---

# 39. Session Validation Service

خدمة مثل:

```text
ISessionAccessValidator
```

تأخذ:

```text
sub
sid
sv
nowUtc
```

وتتحقق من IdentityAccess persistence فقط.

ولا تنفذ Institution Authorization.

---

# 40. JwtBearer Configuration

الـbaseline:

```text
ValidateIssuer = true
ValidateAudience = true
ValidateIssuerSigningKey = true
ValidateLifetime = true

RequireExpirationTime = true
RequireSignedTokens = true

ValidAlgorithms = [RS256]
ValidTypes = [educenteros-access+jwt]
ClockSkew = centrally configured small value

MapInboundClaims = false
SaveToken = false
IncludeErrorDetails = false
```

مع Algorithm/Type/known-`kid` validation الصريحة وRequired claims validation المذكورة في القسم 8.

لا نعتمد Default clock skew بدون قرار؛ القيمة تضبط صراحة وتدخل حساب key-retention وeffective expiration tests.

---

# 41. Security Configuration

تستخدم Options صغيرة حسب المسؤولية، مثل:

```text
AccessTokenOptions
SessionOptions
AccountLockoutOptions
LoginAbuseProtectionOptions
RefreshCredentialOptions
BrowserRefreshCookieOptions
MfaOptions
StepUpOptions
```

والـDomain لا تعتمد على `IOptions` أو `IConfiguration`.

---

# 42. Security Baselines

القيم مثل:

```text
Access Token lifetime
Session idle timeout
Absolute session lifetime
Lockout attempts
Lockout duration
Clock skew
Login abuse windows/delays
TOTP step/window
Recovery code policy
Step-up lifetime per risk class
```

هي **technical security baselines قابلة للConfiguration**، وليست Business rules موزعة داخل الكود.

أي تغيير أمني جوهري في الـprotocol نفسه يحتاج Decision جديدة.

---

# 43. Signing Keys

RSA Private Key تعتبر Secret.

ممنوع وجودها في:

```text
Git
source code
committed appsettings
Dockerfile
logs
```

Key provider يجب أن تدعم:

```text
Current signing key
Current kid
Validation public keys
```

حتى يكون Key Rotation ممكنًا.

قواعد Rotation:

- كل Signing key لها `kid` فريدة.
- Private key الحالية فقط تحتاج signing capability؛ validation تستخدم Public keys.
- المفتاح القديم يظل متاحًا للتحقق مدة لا تقل عن آخر Token أصدرها + configured clock skew.
- Unknown/retired-too-early `kid` لا تعمل fallback صامت.
- Rotation لا تمدد عمر أي Token قائمة.
- عمليات إضافة/تفعيل/إيقاف المفاتيح Security events قابلة للمراجعة.

---

# 44. Persistence

T13 تضيف:

```text
identity_access.user_sessions
identity_access.refresh_token_records
identity_access.mfa_methods
identity_access.recovery_codes
identity_access.mfa_login_challenges
identity_access.step_up_grants
identity_access.operation_authorization_tickets
```

## user_sessions

```text
id
user_account_id
security_version_at_authentication

created_at_utc
authenticated_at_utc
last_primary_authenticated_at_utc
last_seen_at_utc

idle_expires_at_utc
absolute_expires_at_utc

revoked_at_utc?
revocation_reason?

assurance_level
mfa_satisfied_at_utc?

client_type?
device_display_name?
user_agent_summary?
created_from_ip?
last_seen_from_ip?

version
```

## refresh_token_records

```text
id
user_session_id
token_hash

created_at_utc
expires_at_utc

consumed_at_utc?
revoked_at_utc?

replaced_by_token_id?

version
```

## mfa_methods

```text
id
user_account_id
method_type                 // TOTP in Core V1
status                      // Pending / Active / Revoked
encrypted_secret
encryption_key_version
created_at_utc
verified_at_utc?
revoked_at_utc?
last_accepted_time_step?
version
```

## recovery_codes

```text
id
user_account_id
mfa_method_id
generation_id
code_hash
hash_key_version
created_at_utc
consumed_at_utc?
revoked_at_utc?
version
```

## mfa_login_challenges

```text
id
user_account_id
challenge_handle_hash
created_at_utc
expires_at_utc
attempt_count
consumed_at_utc?
version
```

## step_up_grants

```text
id
user_session_id
purpose
target_resource_id?
achieved_assurance_level
issued_at_utc
expires_at_utc
consumed_at_utc?
version
```

## operation_authorization_tickets

```text
id
step_up_grant_id
ticket_handle_hash
hash_key_version
user_account_id
user_session_id
target_module
operation_code
target_resource_id?
operation_id
achieved_assurance_level
issued_at_utc
expires_at_utc
revoked_at_utc?
version
```

Raw refresh credentials وRaw recovery codes وRaw operation ticket handles لا تحفظ. TOTP secret تحفظ encrypted وليست hashed لأنها مطلوبة للتحقق، مع managed key versioning ومنع ظهورها في Logs أو Queries العامة.

## Persistence Constraints & Indexes

الحد الأدنى، باستخدام Database constraints حيث يمكن التعبير عنها، وtransactional invariants حيث تحتاج مقارنة عبر rows/tables:

```text
user_sessions.user_account_id
→ FK to identity_access.user_accounts(id)

refresh_token_records.user_session_id
→ FK to identity_access.user_sessions(id)

refresh_token_records.replaced_by_token_id
→ self FK to identity_access.refresh_token_records(id)

mfa_methods.user_account_id
→ FK to identity_access.user_accounts(id)

recovery_codes.user_account_id
→ FK to identity_access.user_accounts(id)

recovery_codes.mfa_method_id
→ FK to identity_access.mfa_methods(id)

mfa_login_challenges.user_account_id
→ FK to identity_access.user_accounts(id)

step_up_grants.user_session_id
→ FK to identity_access.user_sessions(id)

operation_authorization_tickets.user_account_id
→ FK to identity_access.user_accounts(id)

operation_authorization_tickets.user_session_id
→ FK to identity_access.user_sessions(id)

operation_authorization_tickets.step_up_grant_id
→ FK to identity_access.step_up_grants(id)

UNIQUE(refresh_token_records.token_hash)
UNIQUE(recovery_codes.code_hash)
UNIQUE(mfa_login_challenges.challenge_handle_hash)
UNIQUE(operation_authorization_tickets.ticket_handle_hash)
UNIQUE(operation_authorization_tickets.step_up_grant_id)
at most one Active TOTP method per user_account_id

idle_expires_at_utc <= absolute_expires_at_utc
authenticated_at_utc <= last_primary_authenticated_at_utc
created_at_utc <= authenticated_at_utc
step_up_grants.issued_at_utc < step_up_grants.expires_at_utc
operation_authorization_tickets.issued_at_utc < operation_authorization_tickets.expires_at_utc
mfa_login_challenges.attempt_count >= 0
step_up_grants.expires_at_utc <= owning session absolute_expires_at_utc
operation_authorization_tickets.expires_at_utc <= owning session absolute_expires_at_utc
operation_authorization_tickets.expires_at_utc <= source step_up grant expires_at_utc

revoked session cannot be reactivated
consumed refresh/recovery/step-up proof cannot be consumed again
```

نضيف Indexes لعمليات:

```text
active sessions by user_account_id
refresh lookup by token_hash
session refresh history
active MFA methods by user_account_id
unused recovery codes by generation/account
MFA login challenge lookup by handle_hash
active step-up grants by session/purpose/target
operation ticket lookup by ticket_handle_hash
operation tickets by session/operation_id
```

الـexact PostgreSQL concurrency constraints/locking strategy التي تمنع Refresh fork وdouble consumption تحسمها T16، لكن الـinvariants السابقة غير قابلة للتخفيف.

---

# 45. Concurrency-sensitive Flows

لازم Integration Tests حقيقية على PostgreSQL تغطي:

```text
Refresh vs Refresh
Refresh vs Logout
Refresh vs Logout All
Refresh vs Security Reset
MFA enrollment confirmation vs replacement
MFA login challenge vs same MFA login challenge
Same TOTP time-step vs same TOTP time-step
Recovery code vs same recovery code
Step-up grant vs same step-up grant
One StepUpGrant vs concurrent ticket issuance
Operation ticket vs concurrent use for the same intent
Operation ticket with wrong session/module/operation/resource/OperationId
Target transaction failure then same-intent retry before expiry
Login failure vs concurrent login failure
```

ولا يمكن أن ينتج:

```text
Two usable descendants from one refresh
Revived revoked session
Access after SecurityVersion invalidation
Two successful consumptions of one recovery code
Two successful acceptances of one TOTP time-step
Two successful consumptions of one single-use StepUpGrant
Two OperationAuthorizationTickets from one StepUpGrant
One OperationAuthorizationTicket executing two different intents
Lost failed-login increments
```

التفاصيل العامة للConcurrency تتبع T16.

---

# القواعد النهائية

1. Access JWT قصيرة العمر.
2. UserSession هي Server-side revocation boundary.
3. Refresh Credential opaque وعشوائية.
4. Raw Refresh لا تخزن.
5. Refresh Single-use.
6. Reuse تبطل Session.
7. لا Grace Period في V1.
8. JWT لا تحمل Institution Authorization state.
9. لا يوجد AccountMode.
10. `SecurityVersion` تحمي Account-wide security epoch.
11. `SecurityVersionAtAuthentication` snapshot لا تتغير أثناء حياة Session؛ Authentication evidence الأخرى لا تتجاوز قواعد MFA/Step-up.
12. Protected Request تحتاج JWT + Session + Account validation.
13. Authentication Assurance منفصلة عن Authorization.
14. MFA/Recent Authentication لا تمنح Permission.
15. `ICurrentActor` تحتوي IDs فقط.
16. Domain/Handlers لا تعتمد على HttpContext.
17. Web Access Token = Memory only.
18. Web Refresh = HttpOnly Secure SameSite Strict Cookie.
19. Business APIs = Bearer.
20. Authentication failures الأمنية Fail Closed.
21. Secrets لا تدخل Git أو Logs.
22. Authentication race tests تستخدم PostgreSQL الحقيقية.
23. Core V1 sensitive access تستخدم TOTP MFA وhashed single-use Recovery Codes.
24. SMS OTP ليست permanent second factor للأدوار الحساسة.
25. Step-up proof قصيرة العمر ومرتبطة بالـSession والPurpose والTarget عند الحاجة.
26. لا يوجد Session-wide elevation يفتح كل العمليات الحساسة.
27. Login abuse protection تستخدم أكثر من Signal ولا تعتمد على lockout وحدها.
28. JWT claims المطلوبة وAlgorithm/Type/Key validation صريحة.
29. Refresh expiry لا تتجاوز effective Session expiration.
30. Browser logout يبطل Session ويمسح Cookie المحلية؛ الإبطال Server-side هو Source of Truth.
31. Device metadata Signals للعرض/الأمان وليست Proof أو Authorization.
32. Core V1 Browser-only في token storage؛ أي Native client تحتاج قرارًا مكملًا.
33. الحساب ذو MFA فعالة لا يحصل على Session/Tokens قبل نجاح العامل الثاني.
34. تعطيل/استبدال MFA يزيد `SecurityVersion` ويبطل Sessions.
35. Browser logout-current لا تتعطل لمجرد انتهاء Access JWT؛ يمكنها استخدام Refresh Credential الحالية بأمان.
36. High-risk StepUpGrant تستهلك داخل IdentityAccess وتصدر `OperationAuthorizationTicket` قصيرة ومقيدة بالعملية.
37. الـBusiness effect يثبت `TicketId + OperationId` ذريًا داخل Target Module؛ لا توجد Cross-module transaction مع IdentityAccess.

---

# خارج نطاق T13

```text
Institution Roles
Branch Scope
Capabilities
Resource Authorization
Eligibility
Conflict / Approval Rules
Step-up policy
→ T14

Trusted Devices
WebAuthn / Passkeys
Additional authenticator methods
→ later Identity/Security features

General concurrency strategy
→ T16

OAuth / OIDC / SSO
→ future decision if required

Production secret vault
Automated key rotation
→ Security / Configuration decisions
```

---

# القرار النهائي المختصر

> EduCenterOS تستخدم Authentication مخصصة لنظام First-party، مبنية على `UserSession` Server-side قابلة للإبطال، وJWT Access Token قصيرة العمر، وOpaque Refresh Credential أحادية الاستخدام تعمل Rotation مع Reuse Detection.

> كل Protected Request تحتاج Cryptographic JWT validation ثم Server-side Session/Account validation، مع invariant ثابتة: `JWT.sv == Session.SecurityVersionAtAuthentication == Account.SecurityVersion`.

> Authentication تحتفظ كذلك بـ**Authentication Assurance evidence** مثل وقت الـAuthentication ومستوى الإثبات وMFA satisfaction، بينما T14 هي التي تقرر ما الـAssurance المطلوبة للعملية. هذه المعلومات لا تمنح Role أو Permission.

> Core V1 تنفذ TOTP وRecovery Codes للأدوار والصلاحيات الحساسة. العمليات عالية الخطورة لا تعتمد على رفع Session عامة؛ تستهلك `StepUpGrant` قصيرة العمر داخل IdentityAccess وتستلم Target Module `OperationAuthorizationTicket` أقصر عمرًا ومقيدة بالـSession والعملية والـResource و`OperationId`.

> لا يوجد أي `AccountMode` في Authentication، والـJWT لا تستخدم كـcache للـRoles أو Institution access. `ICurrentActor` تظل مقتصرة على `UserAccountId` و`UserSessionId`.
