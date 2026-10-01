# T12 - IdentityAccess Implementation

## القرار

EduCenterOS يستخدم:

```text
Custom IdentityAccess Domain
+
Selective ASP.NET Core Identity Security Primitives
+
Custom EF Core Persistence
+
Phone-first Registration
+
OTP Challenge + Verification Proof
+
UserAccount / PersonIdentity separation
+
Explicit Account Security State
```

ولا يستخدم:

```text
IdentityUser
IdentityDbContext
AspNetUsers / AspNetRoles
Default Identity UI
Default Identity APIs
UserManager as the application/domain center
```

---

# 1. مسؤولية IdentityAccess

`IdentityAccess` تملك:

```text
UserAccount
PersonIdentity

AccountStatus
SecurityVersion

Phone Verification
Email Verification

Password Credential State
OtpChallenge
VerificationProof lifecycle

UserSession
MfaMethod
RecoveryCode
StepUpGrant
OperationAuthorizationTicket
TrustedDevice

Platform-level identity/security state
```

ملكية المفهوم لا تعني تنفيذ كل Feature من أول Sprint.

---

# 2. IdentityAccess لا تملك

لا تملك:

```text
InstitutionMembership
Institution Roles
BranchScope
EnabledCapabilities

StudentProfile
InstitutionStudentRecord
GuardianRelationship

IndependentTeacherProfile
```

الـownership:

```text
Institution access
→ Institutions

Student / Guardian relationships
→ Students

Independent marketplace teacher
→ Marketplace — Phase 2
```

---

# 3. لا يوجد AccountMode

يتم حذف:

```text
AccountMode
GeneralMode
StudentMode
StudentMode → GeneralMode transition
```

نهائيًا من الـDomain والـDatabase والـAuthorization Model.

---

# 4. الحساب لا يحدد "نوع الشخص"

`UserAccount` تجيب على:

> كيف يدخل الشخص إلى EduCenterOS؟

ولا تجيب على:

> ما كل صفاته وأدواره داخل المنصة؟

بالتالي نفس الشخص يمكن — حسب الـBusiness eligibility — أن يكون لديه:

```text
UserAccount
+
StudentProfile
+
GuardianRelationship
+
InstitutionMembership
+
IndependentTeacherProfile
```

بدون أي `Mode` حصرية تمنع التعايش.

وجود `StudentProfile` مثلًا:

```text
≠ يمنع Staff Membership
≠ يمنع GuardianRelationship
≠ يمنح Institution Permission
```

---

# 5. UserAccount

`UserAccount` هي Aggregate Root للحساب وحالته الأمنية.

الشكل المفاهيمي:

```text
UserAccount

Id
PersonIdentityId

PhoneNumber
NormalizedPhoneNumber
PhoneVerifiedAtUtc

EmailAddress?
NormalizedEmailAddress?
EmailVerifiedAtUtc?

PasswordHash
PasswordChangedAtUtc

Status
InitialOnboardingIntent?

SecurityVersion

AccessFailedCount
LockoutEndUtc?

Version
CreatedAtUtc
```

لا تحتوي:

```text
AccountMode
InstitutionId
MembershipId
Role
BranchScope
Capabilities
StudentProfileId as account type
Guardian role
```

---

# 6. AccountStatus

الـbaseline:

```text
Active
Suspended
Closed
```

## Active

تسمح للحساب باستخدام الـAuthentication بصورة طبيعية، لكنها لا تعني أن كل Login تنجح.

## Suspended

تمنع Authentication وتعتبر Security-sensitive lifecycle state.

الانتقال إلى `Suspended` يجب أن يؤدي ذريًا إلى:

```text
SecurityVersion++
Revoke / invalidate all active sessions
Suspension metadata
```

ولا تعيد إعادة تفعيل الحساب صلاحية أي Session قديمة.

## Closed

تمنع Login العادية.

الانتقال إلى `Closed` يجب أن يؤدي ذريًا إلى:

```text
SecurityVersion++
Revoke / invalidate all active sessions
```

لكن:

```text
Closed ≠ Hard Delete
```

ولا تحذف السجلات التاريخية أو المالية المرتبطة بالشخص.

---

# 7. Temporary Lockout

Temporary Login Lock ليست `AccountStatus`.

تمثل من خلال:

```text
AccessFailedCount
LockoutEndUtc?
```

وتفاصيل Login/Lockout الفعلية يحسمها T13.

---

# 8. SecurityVersion

`SecurityVersion` تستخدم لإبطال الثقة الأمنية القديمة بعد تغييرات Account-wide محددة.

مثل:

```text
Password reset
Account recovery
Phone credential change
Account security reset
MFA disable / replacement
Suspension
Closure
```

ولا تتغير بسبب:

```text
StudentProfile creation
GuardianRelationship
InstitutionMembership
Role change
BranchScope change
```

لأن دي ليست Account-wide Authentication state.

`SecurityVersion` منفصلة عن:

```text
Version
```

الخاصة بالـOptimistic Concurrency.

---

# 9. PersonIdentity

`PersonIdentity` منفصلة عن `UserAccount`.

```text
UserAccount
→ authentication/security account

PersonIdentity
→ underlying person identity
```

في Registration الأساسية الحالية يتم إنشاء:

```text
UserAccount
+
PersonIdentity
```

داخل Transaction واحدة.

لكن:

> وجود Student أو Guardian أو Institution record لا يعني إنشاء UserAccount تلقائيًا.

## قرار دورة الحياة

النموذج المعتمد هو:

```text
PersonIdentity
    1
    │
    └── 0..1 UserAccount
```

يعني:

- `PersonIdentity` يمكن أن توجد بدون `UserAccount`.
- كل `UserAccount` يجب أن ترتبط بـ`PersonIdentity` واحدة.
- نفس `PersonIdentity` لا ترتبط بأكثر من `UserAccount` واحد طوال دورة حياتها؛ الحساب المغلق يعالج بالـRecovery/Reactivation Policy ولا ينشأ حساب موازٍ لنفس الشخص.
- إغلاق الحساب لا يحذف `PersonIdentity` أو التاريخ المرتبط بها.
- إنشاء Student أو Staff record بدون Login يمكن أن يستخدم `PersonIdentity` موجودة أو ينشئ واحدة من خلال Use Case مخولة، بدون إنشاء Credentials.

لا يتم إنشاء أو دمج `PersonIdentity` اعتمادًا على تشابه الاسم فقط.

ربط Account جديدة بـ`PersonIdentity` موجودة يحتاج Linking Workflow صريحة وإثباتًا مناسبًا حسب مصدر السجل والمخاطر.

---

# 10. UserAccount وPersonIdentity IDs

كل واحدة لها:

```text
Guid
UUID v7
```

مستقلة.

لا نستخدم Shared Primary Key.

الربط داخل IdentityAccess يكون Reference صريحة من الحساب إلى الشخص:

```text
UserAccount.PersonIdentityId
```

مع:

```text
NOT NULL
UNIQUE
FOREIGN KEY → identity_access.person_identities(id)
```

وبذلك:

```text
PersonIdentity بدون Account
→ مسموح

UserAccount بدون PersonIdentity
→ غير مسموح

أكثر من UserAccount لنفس PersonIdentity
→ غير مسموح
```

---

# 11. PersonIdentity Data

الـRegistration تجمع الحد الأدنى المطلوب فقط.

مثل:

```text
FullName
```

ولا نجمع تلقائيًا:

```text
NationalId
Identity Document
Passport
Nationality
BirthDate
```

إلا عندما توجد Use Case حقيقية تحتاجها.

Contact verification لا تعني تلقائيًا Legal Identity Verification.

---

# 12. InitialOnboardingIntent

`InitialOnboardingIntent` اختيارية وتستخدم للـRouting/UX فقط.

أمثلة مفاهيمية عندما توجد الـflows:

```text
Student
Guardian
InstitutionOwner
InstitutionInvitee
IndependentTeacher — Phase 2
```

لكن:

```text
OnboardingIntent
≠ Role
≠ Permission
≠ Access Relationship
≠ Account Type
```

مثال:

```text
Intent = InstitutionOwner
```

لا تنشئ `PrimaryOwner`.

ومثال:

```text
Intent = Student
```

لا تغير الحساب إلى Student Mode؛ Module `Students` هي التي تنشئ `StudentProfile` عند نجاح الـOnboarding.

---

# 13. ASP.NET Core Identity

نستخدم Security primitives المجربة عندما تضيف قيمة.

أهمها:

```csharp
IPasswordHasher<UserAccount>
```

باستخدام:

```csharp
PasswordHasher<UserAccount>
```

لكن لا نستخدم ASP.NET Core Identity كـDomain Model.

## 13.1 Why not ASP.NET Core Identity?

عدم استخدام:

```text
IdentityUser
IdentityDbContext
UserManager as the application/domain center
```

ليس هدفه إعادة اختراع الـAuthentication أو كتابة Cryptography مخصصة.

السبب أن نموذج EduCenterOS يفصل بوضوح بين:

```text
UserAccount
→ Credentials / authentication / sessions / security state

PersonIdentity
→ الشخص نفسه

InstitutionMembership
→ الدور والصلاحيات داخل المؤسسة

StudentProfile / GuardianRelationship
→ الصفات والعلاقات التعليمية
```

كما أن المنصة تحتاج Flows مخصصة مثل:

```text
Phone-first registration
OTP Challenge + VerificationProof
Custom session lifecycle
SecurityVersion
Institution / Student / Guardian authorization outside the account
```

استخدام Default Identity Domain Model كمركز للتطبيق قد يدفع إلى وضع Roles وClaims وعلاقات Business متغيرة داخل Account model أو الاعتماد على Abstractions لا تمثل الـDomain الحالية بدقة.

لذلك نستخدم Custom Domain وCustom Persistence مع الاستفادة من Security Primitives مجربة، مثل:

```text
IPasswordHasher<UserAccount>
ASP.NET Core authentication handlers
Cryptographically secure random generation
Framework cryptography APIs
Rate limiting primitives
Data protection primitives when appropriate
```

القواعد:

- لا نكتب Password hashing algorithm مخصصة.
- لا نكتب Encryption أو Random Number Generator مخصصين.
- لا نستخدم Domain purity كسبب لرفض Security primitive مجربة.
- أي Custom security protocol يحتاج Threat Model واختبارات ومراجعة واضحة.
- لو ظهر أن Primitive أو Store جاهزة يمكن استخدامها كـInfrastructure Adapter بدون كسر الـDomain، يسمح بذلك بقرار موثق.

---

# 14. Password Storage

ممنوع تخزين:

```text
Raw Password
Reversibly Encrypted Password
Separate manually-managed Salt
```

نخزن:

```text
PasswordHash
```

فقط.

والـDomain لا تنفذ Password Cryptography بنفسها.

---

# 15. Password Verification & Rehash

نحافظ على نتائج الـPassword Hasher مثل:

```text
Failed
Success
SuccessRehashNeeded
```

ولا نحولها إلى `bool` فقط.

لو:

```text
SuccessRehashNeeded
```

يتم Rehash بطريقة آمنة بعد Authentication ناجحة.

---

# 16. Phone Registration

الـbaseline الحالية:

```text
Phone-first registration
```

رقم الهاتف:

```text
string
```

ويتم Normalize مركزيًا إلى Canonical representation.

للـEgypt V1 مثلًا:

```text
+201xxxxxxxxx
```

قاعدة البيانات تحمي:

```text
UNIQUE(normalized_phone_number)
```

ولا يستخدم Phone Number كـPrimary Key.

---

# 17. Email

Email اختيارية.

ولا تصبح Login Identifier إلا بعد Verification.

القرار النهائي:

```text
normalized_email_address
→ UNIQUE دائمًا عند وجودها، حتى قبل Verification
```

في PostgreSQL يسمح الـUnique Constraint بوجود أكثر من `NULL`، لكنه يمنع تكرار أي قيمة normalized غير فارغة.

التحقق من البريد يغير حالة الثقة فيه، لكنه لا يغير قاعدة uniqueness.

Phone وEmail Contacts وليست Internal IDs.

---

# 18. OTP Challenge

نستخدم Aggregate واحدة:

```text
OtpChallenge
```

مع Purpose واضحة مثل:

```text
RegisterAccount
ChangePhone
VerifyEmail
PasswordReset
StepUpAuthentication
```

والـPurpose تأتي من الـUse Case نفسها، وليس من Client-controlled generic input.

---

# 19. OTP Security

الـOTP:

```text
Generated using CSPRNG
Never stored raw
Never logged raw
```

قاعدة البيانات تخزن:

```text
CodeHash
HashKeyVersion
```

يستخدم Keyed Hash/HMAC مع Server-side secret بدل Hash عادية، لأن OTP البشرية منخفضة الـEntropy ويمكن تجربة كل قيمها لو تسربت قاعدة البيانات.

الـbaseline المفاهيمية:

```text
HMAC(
    serverKey,
    challengeId + purpose + normalizedTarget + otpCode)
```

مع القواعد التالية:

- المفتاح يأتي من Secret Store ولا يدخل Git أو Database.
- `HashKeyVersion` تسمح بتدوير المفتاح بصورة منظمة.
- المقارنة تتم Constant-time.
- نفس الكود لا ينتج Proof قابلة للنقل بين Challenge أو Purpose أو Target مختلفة.
- توليد الكود يستخدم CSPRNG بدون modulo bias.

Challenge تتحكم في:

```text
Expiration
Attempts
Locking
Resend
Single-use verification
Invalidation
```

الأرقام مثل lifetime وattempt limit وcooldown تكون Security Configuration مركزية وليست Business constants داخل الـHandlers.

## OTP Resend وActive Challenge

لكل:

```text
Purpose + NormalizedTarget
```

توجد Challenge واحدة قابلة للاستخدام في نفس الوقت ضمن الـbaseline.

عند إصدار كود جديد:

- يتم إبطال أي Challenge أقدم غير مستهلكة لنفس Purpose وTarget.
- الكود القديم يفشل حتى لو لم تنته مدته الأصلية.
- Resend لا تعيد ميزانية المحاولات أو الإرسال بلا حدود.
- Counters/quotas الممتدة عبر أكثر من Challenge لا تحفظ داخل Challenge الجديدة فقط.

## OTP Rate Limiting وAbuse Protection

Request وVerify تخضعان لسياسة مركزية تشمل حسب الحاجة:

```text
Normalized target
IP / network signal
Device / client signal
Purpose
Time window
Provider and global quota
```

القواعد:

- Rate limiting لا تعتمد على IP وحده.
- نجاح أو فشل Request لا يكشف بلا حاجة هل الحساب موجود.
- الـHTTP status والـresponse body والتوقيت لا تتعمد كشف Account existence.
- Provider failure لا يؤدي إلى إصدار عدد غير محدود من الأكواد.
- Security monitoring تسجل metadata اللازمة بدون OTP raw أو Proof raw.
- القيم النهائية للحدود تضبط في Security Configuration وتختبر قبل Production.

---

# 20. VerificationProof

نجاح OTP لا يستخدم مباشرة كـRegistration Credential دائمة.

بل:

```text
OTP verified
↓
Generate short-lived VerificationProof
↓
Return raw proof once
↓
Store ProofHash only
```

الـProof تكون Secret عالية الـEntropy وليست رقمًا قصيرًا للمستخدم.

الـbaseline:

```text
256 random bits generated by CSPRNG
→ Base64Url for transport
→ Hash / HMAC for storage
```

الـProof مرتبطة بـ:

```text
Purpose
Target
Expiration
Challenge
```

وتستخدم مرة واحدة فقط.

كما تطبق القواعد التالية:

- لا توضع في URL أو Query String.
- لا تخزن في Cookie دائمة.
- لا تدخل Logs أو Analytics أو Error details.
- تتم مقارنتها Constant-time.
- استهلاكها يتم ذريًا مع العملية المقصودة.
- فشل العملية قبل Commit لا يترك Account منشأة مع Proof قابلة لإعادة الاستخدام بصورة غير مضبوطة.

---

# 21. Verified vs Consumed

نفرق بين:

```text
Verified
→ ownership/contact proof succeeded

Consumed
→ that proof has already been used by its intended operation
```

وده يمنع Replay.

---

# 22. Registration Flow

الـflow الأساسية:

```text
RequestPhoneVerification
↓
Normalize + validate phone
↓
Apply rate limits / abuse policy
↓
Invalidate older active challenge for same Purpose + Target
↓
Create OtpChallenge
↓
Generate OTP
↓
Store CodeHash
↓
Send OTP
↓

VerifyPhone
↓
Validate OTP/challenge
↓
Increment attempts atomically on failure
↓
Mark verified atomically
↓
Generate VerificationProof
↓
Store ProofHash
↓
Return raw proof once
↓

RegisterAccount
↓
Validate Challenge + Proof + Purpose + Expiry
↓
Read verified phone from Challenge
↓
Check uniqueness
↓
Validate password
↓
Generate UUID v7 IDs
↓
Hash password
↓
Create UserAccount
+
Create PersonIdentity
+
Consume VerificationProof
↓
Commit one transaction
```

المسار السابق هو Standalone Registration لشخص لا توجد `PersonIdentity` معروفة له.

أما لو الحساب يتم تفعيله لشخص له `PersonIdentity` موجودة بالفعل، مثل طالب أنشأته المؤسسة بدون Login، نستخدم Linking/Activation Flow مستقلة:

```text
Verify account contact
↓
Verify identity-link evidence appropriate to the use case
↓
Load existing PersonIdentity
↓
Ensure it has no UserAccount
↓
Create UserAccount linked to existing PersonIdentity
+
Consume verification/linking proofs
↓
Commit one transaction
```

في هذا المسار لا ننشئ `PersonIdentity` ثانية تلقائيًا، ولا نربط بالاسم أو Contact واحدة غير كافية.

تفاصيل الـCross-Module linking evidence والـContracts تتبع T18 والـFeature المالكة.

---

# 23. Registration Source of Truth

`RegisterAccount` لا تثق في Phone القادمة مرة أخرى من Request.

الموبايل المستخدم لإنشاء الحساب يأتي من:

```text
Verified OtpChallenge.Target
```

---

# 24. New Account Initial State

الحساب الجديد يبدأ:

```text
PersonIdentityId = verified/new identity
Status = Active
InitialOnboardingIntent = null
SecurityVersion = 1
AccessFailedCount = 0
```

ولا يوجد:

```text
Mode
GeneralMode
StudentMode
```

---

# 25. Registration لا تعمل Login

بعد نجاح Registration:

```text
UserAccount created
PersonIdentity created
```

لكن لا تعمل:

```text
Create UserSession
Issue Access Token
Issue Refresh Token
```

الـLogin والجلسات والـcredentials تتبع T13.

---

# 26. Persistence Baseline

الـSchema:

```text
identity_access
```

أول Tables المطلوبة:

```text
user_accounts
person_identities
otp_challenges
```

ولا ننشئ Tables مؤجلة قبل Feature تحتاجها.

---

# 27. user_accounts

الـbaseline:

```text
id
person_identity_id

phone_number
normalized_phone_number
phone_verified_at_utc

email_address?
normalized_email_address?
email_verified_at_utc?

password_hash
password_changed_at_utc

account_status
initial_onboarding_intent?

security_version

access_failed_count
lockout_end_utc?

version
created_at_utc
```

القيود الأساسية:

```text
person_identity_id NOT NULL UNIQUE
normalized_phone_number NOT NULL UNIQUE
phone_verified_at_utc NOT NULL
normalized_email_address UNIQUE when non-null
password_hash NOT NULL
security_version >= 1
access_failed_count >= 0
```

لا يوجد:

```text
account_mode
```

---

# 28. person_identities

الـbaseline الأولية:

```text
id
full_name
version
created_at_utc
```

ولا تحتوي `user_account_id`.

وجود Account من عدمه يحدد من الـReference والـUnique Constraint داخل `user_accounts.person_identity_id`.

أي بيانات هوية رسمية أو Verification metadata لا تضاف للجدول إلا عند تنفيذ Use Case تحتاجها وبحسب قواعد الخصوصية.

---

# 29. otp_challenges

تحتوي Concepts مثل:

```text
id

purpose
channel
target

code_hash
hash_key_version

created_at_utc
expires_at_utc
resend_available_at_utc

attempt_count
max_attempts

verified_at_utc?
locked_at_utc?
invalidated_at_utc?

proof_hash?
proof_expires_at_utc?
consumed_at_utc?

version
```

تضاف Constraints مناسبة تمنع الحالات المستحيلة، لكن Domain methods تظل المسؤولة الأساسية عن Lifecycle مثل:

```text
Verified only once
Consumed only after Verified
Invalidated / Locked challenge cannot verify
Consumed proof cannot be reused
```

---

# 30. Cross-Module Access

أي Module أخرى:

```text
❌ لا تستخدم IdentityAccessDbContext
❌ لا تعدل identity_access tables
❌ لا تستخدم IdentityAccess entities مباشرة
```

التعامل يتم عبر Contracts ضيقة ومعلنة.

ولا ننشئ:

```text
IUserService
IIdentityService
```

ضخمة تجمع كل شيء.

تفاصيل الـContracts تتبع T18.

---

# 31. Student without Login

يظل صحيحًا:

```text
InstitutionStudentRecord
or
StudentProfile
```

يمكن أن توجد بدون:

```text
UserAccount
```

وفي Model المعتمد يمكن أن ترتبط بـ`PersonIdentity` بدون أن يكون لهذه الهوية Account.

وعمل Login للطالب لاحقًا يكون Linking/Onboarding Use Case صريحة، وليس إنشاء Account تلقائيًا لكل Student record.

---

# 32. Guardian Relationship

`GuardianRelationship` ليست جزءًا من IdentityAccess.

IdentityAccess قد تثبت:

```text
Who is the authenticated person?
```

لكن Module `Students` هي التي تملك:

```text
Who is guardian of which student?
Under what scope?
With what assurance?
```

---

# 33. MFA & Sessions

المفاهيم التالية مملوكة معماريًا لـIdentityAccess:

```text
UserSession
MfaMethod
RecoveryCode
TrustedDevice
```

لكن:

```text
Login / Sessions / Access / Refresh
TOTP MFA / Recovery Codes / Step-up grants / operation authorization tickets
→ T13

Authorization / Step-up requirement
→ T14

Trusted Devices / WebAuthn / additional authenticators
→ later security decisions
```

ولا ننشئ Persistence مؤجلة إلا عند أول Feature تحتاجها.

---

# 34. Tests

T12 يجب أن تغطي على الأقل:

```text
OTP expiration
OTP attempt limits
OTP invalidation
OTP successful verification once
OTP resend invalidates older code
OTP resend does not reset rolling rate limits
OTP HMAC purpose/target/challenge binding
OTP constant-time verification path

Equivalent Egyptian phone formats normalize to one canonical value
Enumeration-safe external responses

VerificationProof single-use
Concurrent proof consumption
VerificationProof purpose/target/challenge binding
VerificationProof never appears in logs

Duplicate phone
Concurrent registration
Account + PersonIdentity atomic creation
Existing PersonIdentity account activation
One UserAccount maximum per PersonIdentity
Wrong identity-link evidence rejected

Duplicate normalized email before verification
Concurrent email verification / assignment

Password hashing
No raw secrets persisted

SecurityVersion transitions
Account status transitions
Suspension revokes existing sessions
Closure revokes existing sessions

No AccountMode anywhere
```

وتستخدم Integration Tests PostgreSQL حقيقية.

---

# 35. Security Rules

ممنوع Logging أو Persistence للـ:

```text
Raw Password
OTP Code
VerificationProof
Recovery Code
Refresh Token
Sensitive cryptographic secrets
```

ولا تدخل Production secrets إلى Git.

Security endpoints لا تكشف Account existence أو security state بلا حاجة.

أي Endpoint تستقبل OTP أو VerificationProof تعامل Request body وHeaders المرتبطة بها كبيانات حساسة ولا تسجلها كاملة.

---

# القواعد النهائية

1. IdentityAccess تملك account/identity/authentication security state.
2. لا يوجد `AccountMode`.
3. Student ليست نوع Account حصري.
4. Compatible profiles/relationships/roles يمكن أن تتعايش.
5. `OnboardingIntent` Routing فقط.
6. `UserAccount` و`PersonIdentity` منفصلتان؛ `PersonIdentity` يمكن أن توجد بدون Account.
7. كل `UserAccount` ترتبط بـ`PersonIdentity` واحدة، وبحد أقصى Account واحدة لكل PersonIdentity.
8. Institution roles لا تدخل UserAccount.
9. StudentProfile وGuardianRelationship لا تدخل IdentityAccess.
10. Phone-first registration baseline.
11. Account لا تنشأ قبل Verification المطلوبة.
12. OTP وVerificationProof Single-use.
13. OTP الجديدة تبطل الأقدم لنفس Purpose وTarget، وResend لا تعيد Limits بلا حدود.
14. OTP تستخدم HMAC مرتبطة بالـChallenge/Purpose/Target مع Key Versioning.
15. VerificationProof عالية الـEntropy ولا تنتقل في URL أو تدخل Logs.
16. لا Raw OTP/Proof/Password في DB.
17. `normalized_phone_number` و`normalized_email_address` فريدتان عند وجودهما، حتى قبل Email Verification.
18. Password hashing تستخدم framework security primitives.
19. Registration وIdentity Linking transactions ذرية.
20. Registration لا تعمل Auto-login.
21. Suspension وClosure تبطلان كل Sessions وتزيدان `SecurityVersion`.
22. `SecurityVersion` للأمان، و`Version` للConcurrency.
23. UserSession/MFA owned by IdentityAccess؛ Core V1 baseline تنفذ حسب T13، والطرق المتقدمة تضاف تدريجيًا.
24. Modules الأخرى تتعامل مع IdentityAccess عبر Contracts فقط.
25. High-risk Step-up تنتقل للموديول الهدف بتذكرة Authentication proof مقيدة بالعملية حسب T13/T15، بدون Cross-module transaction.

---

# خارج نطاق T12

```text
Access JWT
Refresh Credentials
Session rotation/revocation
Login implementation
TOTP MFA / Recovery Codes / Step-up grants / operation authorization tickets
→ T13

Institution authorization
Roles / Scope / Capabilities
Student / Guardian resource authorization
Step-up requirements
→ T14

General concurrency policy
→ T16

Cross-module contracts
→ T18

Full security hardening
Exact rate-limit thresholds / storage / distributed enforcement
Recovery hardening
Key management
→ Security decisions
```

---

# القرار النهائي المختصر

> EduCenterOS تستخدم `IdentityAccess` مخصصة بدل Default ASP.NET Core Identity domain model، مع الاستفادة الانتقائية من Security primitives المجربة مثل `IPasswordHasher<UserAccount>`.

> لا يوجد `AccountMode`, `GeneralMode` أو `StudentMode`. الـUserAccount تمثل وسيلة الدخول والحالة الأمنية فقط، بينما StudentProfile وGuardianRelationship وInstitutionMembership وغيرها تظل مفاهيم مستقلة يمكن أن تتعايش حسب الـBusiness rules.

> `PersonIdentity` تمثل الشخص ويمكن أن توجد بدون Login؛ كل `UserAccount` ترتبط بهوية واحدة، وبحد أقصى Account واحدة لكل PersonIdentity. تفعيل حساب لشخص موجود يستخدم Linking Workflow بدل إنشاء هوية مكررة.

> Registration تعتمد Phone Verification باستخدام `OtpChallenge` ثم `VerificationProof` قصيرة العمر وأحادية الاستخدام، مع HMAC للـOTP وHash/HMAC للـProof وعدم تخزين القيم الخام، وإنشاء `UserAccount + PersonIdentity` أو ربط Account بهوية موجودة واستهلاك الإثباتات داخل Transaction واحدة.

> `OnboardingIntent` تستخدم للـUX/Routing فقط ولا تمنح Role أو Permission أو Profile.
