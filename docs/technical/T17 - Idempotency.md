# T17 - Idempotency

## القرار

EduCenterOS يستخدم:

```text id="h7lv9n"
Selective Idempotency
for sensitive mutation endpoints
```

باستخدام:

```text id="e3wtoi"
Idempotency-Key
+
Versioned Trusted Scope Hash
+
Stable OperationId
+
Versioned Semantic Request Fingerprint
+
Module-local Persistence
+
Database Unique Constraint
+
Same Owning Transaction as Business Effect
```

ولا نطبق Idempotency Middleware عامة على كل `POST`.

---

# 1. المشكلة التي تحلها

السيناريو الأساسي:

```text id="v283hf"
Client sends command
↓
Server commits
↓
HTTP response is lost
↓
Client does not know outcome
↓
Client retries same logical intent
```

المطلوب:

> Retry لا تنتج Business Effect ثانية.

---

# 2. Idempotency vs Concurrency

فرق إلزامي:

```text id="5nvlu7"
Idempotency
→ نفس intent وصلت أكثر من مرة

Concurrency
→ intents مختلفة تتنافس على نفس state
```

مثال:

```text id="c1rijz"
User retries same booking after timeout
→ T17 Idempotency

Two different students reserve last seat
→ T16 Concurrency
```

الـBooking قد تحتاج الاثنين معًا.

---

# 3. Idempotency vs Business Uniqueness

`Idempotency-Key` لا تستبدل:

```text id="yovk8j"
Unique Constraints
Domain Invariants
Capacity Rules
Financial Uniqueness
```

لأن Client تستطيع إرسال Key جديدة لنفس Business duplicate.

---

# 4. Idempotency vs Event Deduplication

T17 تخص:

```text id="bnu1hf"
HTTP / Application Command Intake
```

ولا تستخدم كـInbox عامة للـIntegration Events.

Event/Message deduplication تتبع T19.

---

# 5. أين نستخدمها؟

مرشحة بقوة للعمليات التي Duplicate effect فيها مؤذية، مثل:

```text id="xx895d"
Booking creation
Enrollment / Renewal
Payments
Refunds
Financial posting
Paid subscription actions
Import-job creation
External integration commands
Other material create commands
```

لكن القرار يكون **per endpoint**.

## 5.1 Core V1 Endpoint Policy

كل mutation endpoint تعلن صراحة واحدة من:

```text
Required
SemanticallyIdempotent
SecuritySpecific
NotRequiredWithReason
```

الـbaseline المعتمدة:

```text
Create Seat Reservation / material Booking
Confirm Enrollment / Renewal when duplicate effect is material
RecordCashPayment
Refund / Financial reversal commands
Create ImportJob
High-risk OperationAuthorizationTicket-bound mutation
→ Required

Logout / revoke already-revoked target
→ SemanticallyIdempotent

Login / Refresh / OTP / Recovery / Step-up grant consumption
/ OperationAuthorizationTicket issuance
→ SecuritySpecific under T13/T16, not generic T17
```

الـpolicy تكون Version-controlled بجانب Endpoint/Feature tests؛ إضافة mutation حساسة بدون Mode صريحة تفشل Architecture test.

ولكل Endpoint في Mode = `Required` تسجل الـpolicy كذلك:

```text
OperationCode
OperationIdSource = ServerGeneratedAtClaim | CallerSupplied
FingerprintContractVersion
ResultContractVersion
RetentionProfile
RequiresOperationAuthorizationTicket
```

لا تترك هذه القيم لاجتهاد الـHandler؛ تكون Metadata واحدة تقرؤها الـpipeline وتثبتها Architecture/Integration tests.

---

# 6. أين لا نستخدمها تلقائيًا؟

لا نفرض generic Idempotency على:

```text id="1kh7br"
GET / HEAD
Ordinary queries
Naturally-idempotent PUT
Naturally-idempotent DELETE
```

ولا على Security flows التالية:

```text id="by32ns"
Login
Refresh
OTP verification
VerificationProof consumption
Recovery code consumption
StepUpGrant consumption / OperationAuthorizationTicket issuance
```

لأن لها Security semantics خاصة.

---

# 7. Logout

Logout/Revoke-All يمكن أن تكون:

```text id="jbqo0y"
Semantically idempotent
```

بدون Persistent Idempotency Store.

يعني repeated logout لا تحتاج إنشاء HTTP idempotency protocol كاملة.

---

# 8. Idempotency-Key

Endpoint التي تتطلب الحماية تستقبل:

```http id="nx8zer"
Idempotency-Key: <uuid>
```

للـFirst-party clients في V1:

```text id="n2qwwl"
Canonical UUID
Non-empty
Guid.Empty forbidden
Exactly one header value
```

الـClient تولد Key قبل أول Attempt.

---

# 9. قاعدة الـKey الأساسية

```text id="w4fn94"
One logical intent
→ one Idempotency-Key
```

لو Request timeout:

```text id="ddrgnw"
Reuse same Key
```

ولا تولد Key جديدة.

Intent جديدة:

```text id="npts09"
Generate new Key
```

---

# 10. Key ليست Business ID

`Idempotency-Key`:

```text id="sm1jh5"
≠ Booking Code
≠ Payment ID
≠ Entity ID
≠ Correlation ID
≠ Authentication Secret
```

كل مفهوم مستقل.

## 10.1 OperationId

كل Required Idempotency operation المكتملة لها `OperationId` ثابتة تمثل الـbusiness execution، وتظل مختلفة مفهوميًا عن HTTP delivery key:

```text
Idempotency-Key
→ identifies repeated HTTP delivery of one intent

OperationId
→ identifies the durable business operation/result
```

مصدرها واحد من:

```text
ServerGeneratedAtClaim
CallerSupplied
```

القواعد:

- العمليات العادية يولد لها السيرفر/Database UUID v7 **كجزء من idempotency claim insert** وتعود مع `RETURNING` قبل الـbusiness mutation، ويحافظ عليها خلال كل internal transaction retries. لو لم يحدث Commit فلا توجد Business operation ملزمة بالحفاظ على ID غير مرئية.
- العمليات المالية الأساسية أو التي تحتاج `OperationAuthorizationTicket` تستقبل Canonical non-empty UUID OperationId من الـClient قبل إصدار Ticket، ثم تستخدم نفس القيمة عند التنفيذ وإعادة المحاولة.
- Caller-supplied OperationId تدخل semantic fingerprint؛ Server-generated OperationId لا تدخل request fingerprint لأنها لم تكن جزءًا من Request، بل تخزن في الـrecord والـbusiness operation بعد claim.
- نفس Key مع Caller-supplied OperationId مختلفة هي `KeyReuseMismatch`.
- نفس OperationId مع Key مختلفة لا تنفذ effect ثانية؛ لو الـscope/fingerprint تطابقا تعاد النتيجة، وإلا `Idempotency.OperationIdReuseMismatch`.
- Caller-supplied OperationId تحفظ كذلك في target business row/operation receipt مع Unique Constraint أطول عمرًا من Idempotency record؛ cleanup لا تزيل منع duplicate business execution.
- Attendance OperationId تظل Durable offline identity بقواعد القسم 41، لكنها تستخدم نفس مبدأ عدم إعادة تطبيق operation واحدة.

---

# 11. Trusted Scope

الـKey وحدها ليست Global.

Lookup identity تتكون من:

```text id="4zmfza"
Operation Code
+
TrustedScopeHash
+
IdempotencyKeyHash
```

`TrustedScopeHash` قيمة `NOT NULL` يبنيها السيرفر من Canonical tuple مُرقّمة:

```text id="76d9a1"
ScopeVersion
CallerKind
UserAccountId
Validated InstitutionId or explicit Global marker
AccessKind / RelationshipId only when they change business actor semantics
```

`InstitutionId` تأتي من Validated Institution Context، وليس Request الخام.

```text
TrustedScopeHash = SHA-256(canonical versioned trusted-scope tuple)
```

لا نضع `UserSessionId` في الـscope الافتراضية حتى يظل replay ممكنًا بعد refresh أو Login جديدة لنفس الحساب، ما لم تكن العملية نفسها Session-scoped بطبيعتها.

نخزن الأعمدة الموثوقة صراحة للتشخيص، لكن الـUnique arbitration تستخدم `TrustedScopeHash` غير القابلة للـNULL لتجنب اختلاف سلوك nullable scopes.

---

# 12. Cross-user / Cross-tenant Isolation

نفس Key:

```text id="5rhiwc"
User A ≠ User B

Institution A ≠ Institution B
```

ولا يجوز أن تؤدي إلى replay بينهما.

---

# 13. Request Fingerprint

كل Idempotency Record تخزن Fingerprint للـsemantic request.

باستخدام:

```text id="abvznb"
SHA-256
```

على Canonical representation واضحة.

مثل:

```text id="ves6cy"
OperationCode
Route semantic parameters
Validated DTO values
Relevant behavior-changing options
```

كل Endpoint Required تعرف `FingerprintContractVersion` وFingerprint DTO خاصة بها. الـcanonical encoding في Core V1:

```text
UTF-8
Fixed/lexicographically ordered property names
Validated values after server default application
Explicit missing-vs-null semantics
UUIDs in lowercase canonical D format
Enums as stable explicit codes
Decimals as invariant scale-insensitive canonical strings
Instants normalized to UTC round-trip representation
Domain-normalized phone/email/codes where applicable
Unordered collections sorted by their semantic key
Uploaded content represented by content hash, not file name alone
```

وتبدأ مادة الـhash بـ:

```text
FingerprintContractVersion
OperationCode
CallerSuppliedOperationId when the endpoint contract has one
Route/resource semantic IDs
Canonical fingerprint DTO
```

لا تدخل فيها Roles/Permissions الحالية أو Correlation/Trace metadata أو Raw Step-up handle؛ Authorization تعاد منفصلة، والـStep-up binding تعتمد على OperationId.

كل Endpoint تحتفظ Golden-vector tests تثبت أن payloads المتكافئة تعطي نفس hash، وأن أي behavior-changing difference تعطي hash مختلفة.

---

# 14. نفس Key + Payload مختلفة

النتيجة:

```text id="jwvfem"
409 Conflict

Idempotency.KeyReuseMismatch
```

ولا نعيد النتيجة القديمة.

ولا نكشف للClient الـpayload الأصلية أو الـfingerprint.

---

# 15. Raw Request لا تخزن

لا نخزن:

```text id="mzvdp5"
Raw HTTP Body
Authorization header
Cookies
Correlation ID
User-Agent
```

لمجرد Idempotency matching.

Fingerprint تكون Semantic ومحددة لكل Endpoint.

---

# 16. تخزين الـKey

لا نحتاج Raw Key في Database.

نخزن:

```text id="f2oqnz"
IdempotencyKeyHash = SHA-256(canonical 16 UUID bytes)
```

ولا نسجل Raw Key في Logs.

الـKey ليست Secret، لكن Hash تقلل unnecessary retention/log leakage.

---

# 17. Module-local Ownership

كل Module تستخدم Idempotency تملك Records الخاصة بها.

مثال:

```text id="f5xhsx"
Enrollments
→ enrollments idempotency records

StudentFinance
→ student_finance idempotency records
```

ولا يوجد:

```text id="2nhs1f"
Global IdempotencyDbContext
```

للنظام كله.

---

# 18. لماذا Module-local؟

لأن المطلوب:

```text id="neie81"
Idempotency reservation
+
Business mutation
+
Replayable result
```

تكون داخل:

> **نفس Owning Transaction**

المعتمدة في T15.

الاستثناء الوحيد هو `RecordCashPayment`: StudentFinance تملك Idempotency record، لكنها تحفظ مع `Payment` وBranchFinance `CashMovement` داخل نفس shared PostgreSQL transaction المعتمدة صراحة في T15. مشاركة Transaction لا تغير ملكية الـrecord أو أي جدول.

---

# 19. Record المفاهيمية

يمكن أن تحتوي:

```text id="zugrzu"
Id
OperationCode
CallerKind
CallerId
InstitutionId?
AccessKind?
RelationshipId?
TrustedScopeVersion
TrustedScopeHash

IdempotencyKeyHash
OperationId
OperationIdSource
FingerprintContractVersion
RequestFingerprint

Status

ResultKind
ResourceId?
ResponseStatusCode
ResultContractVersion
ReplayPayloadJson?

CreatedAtUtc
CompletedAtUtc
ReplayUntilUtc
PurgeAfterUtc
AuthorizationTicketId?
```

`ReplayPayloadJson` ليست Raw HTTP body؛ هي optional allow-listed immutable payload صغيرة. الـdefault هو Result reference/IDs، والحد الأقصى للـpayload المخزنة في Core V1 هو `16 KiB` محسوبة على UTF-8 bytes. أي نتيجة أكبر تخزن Reference فقط.

---

# 20. Unique Constraint

الـDatabase هي الحكم النهائي للـsame-key race.

مثال:

```text id="03emkr"
UNIQUE(
    OperationCode,
    TrustedScopeHash,
    IdempotencyKeyHash
)

UNIQUE(
    OperationCode,
    TrustedScopeHash,
    OperationId
)
```

كل أعمدة الـUnique السابقة `NOT NULL`. الأعمدة التشخيصية مثل `InstitutionId?` لا تدخل constraint بصورة Nullable.

الـUnique indexes/constraints المستخدمة في claim تكون `NOT DEFERRABLE` حتى تستطيع `INSERT ... ON CONFLICT` حسم المنافسة وقت الـclaim نفسه.

ولا نعتمد على:

```text id="wksr5o"
check in memory
→ then insert
```

فقط.

---

## 20.1 Persistence Constraints and Indexes

إلى جانب الـUnique constraints، يفرض الـschema الحد الأدنى التالي:

```text
TrustedScopeHash       = 32 bytes
IdempotencyKeyHash     = 32 bytes
RequestFingerprint    = 32 bytes
OperationId            != 00000000-0000-0000-0000-000000000000
TrustedScopeVersion    > 0
FingerprintContractVersion > 0
ResultContractVersion > 0 when Status = Completed

CreatedAtUtc <= CompletedAtUtc
CompletedAtUtc < ReplayUntilUtc
ReplayUntilUtc < PurgeAfterUtc
```

وعندما تكون `Status = Completed` تكون `CompletedAtUtc` و`ReplayUntilUtc` و`PurgeAfterUtc` و`ResultKind` و`ResponseStatusCode` و`ResultContractVersion` غير قابلة للـNULL. `ReplayPayloadJson` تظل optional، لكن حجمها `<= 16 KiB` يفرض في application validation وDatabase `CHECK` على UTF-8 byte length.

الـindexes التشغيلية الأساسية:

```text
(OperationCode, TrustedScopeHash, IdempotencyKeyHash) UNIQUE NOT DEFERRABLE
(OperationCode, TrustedScopeHash, OperationId) UNIQUE NOT DEFERRABLE
(PurgeAfterUtc) WHERE Status = Completed
```

الـtransaction wrapper تعمل assertion قبل Commit أن synchronous claim التي أنشأتها هذه العملية أصبحت `Completed`؛ وجود committed `Processing` يعتبر invariant violation ويؤدي إلى Rollback.

---

# 21. Processing / Completed

الحد الأدنى المفاهيمي:

```text id="6m8ybz"
Processing
Completed
```

في synchronous commands العادية:

> `Processing` حالة داخل Transaction الجارية فقط، ولا تبقى committed لوحدها.

المسار الصحيح:

```text id="5chd3o"
Begin transaction
↓
Reserve Key
↓
Execute Business
↓
Persist Business State
↓
Mark Idempotency Completed
↓
Commit everything together
```

لذلك كل Idempotency record مرئية committed لعملية synchronous تكون `Completed`. لا نعتمد على قراءة committed `Processing` لاكتشاف السباق؛ السباق تحسمه الـUnique index/lock wait كما في القسم 28.

---

# 22. ممنوع Two-phase Reservation

مرفوض:

```text id="9hc6it"
Commit idempotency reservation
↓
Run business command
↓
Commit result later
```

لأنه قد يترك:

```text id="16of76"
Reserved Key
+
No Business Effect
```

بعد crash.

---

# 23. First Execution

المسار المعتمد:

```text id="1quzps"
Authenticate / establish Institution Context
/ evaluate current base permission and request visibility
↓
Validate Request
↓
Validate Key / OperationId
↓
Build TrustedScopeHash + versioned Fingerprint
↓
Fast-path authoritative-primary lookup by Key
and Caller-supplied OperationId when present
↓
If Completed exists → section 24 replay path
↓
If no record and operation is high-risk
→ validate fresh OperationAuthorizationTicket for first execution
↓
Begin owning transaction
↓
INSERT idempotency record
ON CONFLICT DO NOTHING
RETURNING Id, OperationId
↓
Inserted?
├─ No → read the committed winner and resolve replay/mismatch/expiry
└─ Yes → execute Business Command
↓
Store business state
↓
Store minimal replay-safe result and mark Completed
↓
Commit
↓
Return response
```

القواعد:

- كل lookups المستخدمة لحسم commit ambiguity/replay تكون على Authoritative PostgreSQL primary، وليس Read Replica قابلة للتأخر.
- `ON CONFLICT DO NOTHING` تمنع تحويل transaction الخاسرة إلى aborted state بسبب `23505`.
- لو insert لم تعد row، نبحث داخل trusted scope بالـKey أولًا ثم Caller-supplied OperationId؛ بذلك نفرق بين same-key replay وsame-operation/different-key conflict.
- عند `Read Committed`، لو insert منافسة كانت uncommitted تنتظرها PostgreSQL: لو الفائزة rollback يمكن للطلب الحالي أن يصبح الفائز؛ ولو Commit يرجع insert بدون row ثم تقرأ الـCompleted record.
- لو الانتظار وصل `lock_timeout`، نعمل rollback للـtransaction الحالية ونرجع `Idempotency.RequestInProgress` مع `Retry-After`؛ لا ننفذ الـbusiness effect.
- أي internal deadlock/serialization retry تتبع T16 باستخدام نفس Key وOperationId وscope/fingerprint.

---

# 24. Sequential Replay

لو وجدنا:

```text id="eifnm4"
Same Scope
+
Same Key
+
Same Fingerprint
+
Completed
```

نعيد:

```text id="7lzm9i"
Same semantic result
```

بدون:

```text id="n5crpv"
Business mutation ثانية
Domain Event ثانية
Notification ثانية
Payment call ثانية
Audit event كأن العملية حدثت مرتين
```

---

# 25. Replay ليست نسخة HTTP حرفية

نحافظ على:

```text id="n9r01c"
Same safe semantic result
Same created resource ID
Same operation/job ID
Same success status where appropriate
```

لكن كل Attempt تحصل على:

```text id="230bg5"
Fresh CorrelationId
Fresh traceId
```

الـreplay response تعاد بناؤها من:

```text
ResultKind
ResourceId / OperationId
small immutable ReplayPayload when explicitly allowed
ResultContractVersion adapter
```

ولا نخزن أو نعيد تلقائيًا Raw response headers أو `Set-Cookie` أو transient URLs. لو لم يعد التطبيق يدعم `ResultContractVersion` محفوظة، يفشل Deployment compatibility test؛ لا نخمن تحويلًا أثناء الطلب.

---

# 26. Authorization before Replay

وجود Key صحيحة لا يمنح Access.

كل Retry تعيد:

```text id="r9qu1w"
Authentication
Institution Context
Authorization
```

قبل replay.

الترتيب يفرق بين حالتين:

## Completed Replay

- نعيد Current Authentication وInstitution Context وPermission/Resource visibility.
- لا نعيد Business eligibility/state-transition rules التي تخص First execution؛ العملية المكتملة قد تكون هي نفسها التي غيرت الـstate.
- لا نطلب أن تكون `OperationAuthorizationTicket` الأصلية غير منتهية أو غير مستهلكة؛ هي أثبتت First execution بالفعل.
- لا نحتاج Raw Step-up handle في fingerprint أو replay request.
- `AuthorizationTicketId?` المحفوظة تستخدم Audit/binding trace فقط، بينما Key + trusted scope + fingerprint + OperationId تحسم أن النية نفسها.

## First Execution / No Completed Record

- تطبق كل Current Authorization.
- لو Policy تتطلب Step-up، يجب تقديم Ticket حية ومطابقة للـSession/Module/Operation/Resource/OperationId حسب T13–T16.
- لا يسمح Completed lookup مفقود بتجاوز Fresh assurance المطلوبة.

مثال:

```text id="cnmkhf"
Operation succeeded yesterday
↓
Permission revoked
↓
Same-key retry
↓
403 / 404
```

ولا نعيد stored business data.

---

# 27. لا Replay للSecrets

ممنوع generic stored replay لـ:

```text id="42c91w"
Access Tokens
Refresh Credentials
Cookies
OTP
Verification Proof
Passwords
Recovery Secrets
```

وعشان كده Login/Refresh/OTP خارج generic T17 mechanism.

---

# 28. Concurrent Same-Key Requests

لو Request A وB يصلوا بنفس Key في نفس اللحظة:

```text id="7sk2m1"
Both attempt:
INSERT ...
ON CONFLICT DO NOTHING
RETURNING Id
```

PostgreSQL unique index تجعل المتنافس ينتظر الـuncommitted conflicting transaction:

```text
A commits
→ B insert returns no row
→ B reads Completed winner
→ replay/mismatch resolution

A rolls back
→ B insert may succeed
→ B becomes first executor
```

في الحالتين Business effect لا تنفذ بالتوازي مرتين.

لو B تجاوزت `lock_timeout` أثناء الانتظار:

```text id="mlp2tp"
Rollback B transaction
↓
409
Idempotency.RequestInProgress
+
Retry-After: 1
```

ولا نعمل retry داخلي أعمى؛ الـClient تعيد نفس Key/OperationId. قيمة `Retry-After` HTTP baseline ثانية واحدة ويمكن ضبطها مركزيًا، ولا تعني أن التنفيذ اكتمل حتمًا خلالها.

---

# 29. Commit Succeeded / Response Lost

دي أهم ضمانة.

```text id="y3sajl"
DB Commit succeeds
↓
HTTP response lost
↓
Client retries same Key
↓
Server finds Completed record
↓
Replay same result
```

ولا ننفذ Business Command مرة ثانية.

---

# 30. Commit Ambiguity

لو Connection فشلت حول `COMMIT`:

ممنوع:

```text id="tnrt9l"
Assume rollback
Generate new key
Run command again blindly
```

المسار:

```text id="jb24ti"
Retry same logical request
+
Same key
↓
Authoritative-primary lookup
```

النتائج:

```text
Completed record exists
→ Commit happened
→ replay stored semantic result

No record exists after authoritative connectivity is restored
→ atomic business transaction did not commit
→ same request may execute using the same Key/OperationId

Authoritative outcome still cannot be queried
→ 503 Idempotency.OutcomeUnknown
→ client must retry the same Key/OperationId later
```

ممنوع حسم Commit outcome من Read Replica أو Cache أو memory state.

---

# 31. Failed Attempts

لا نخزن كـCompleted Result افتراضيًا:

```text id="s10snw"
400 Validation
401 Authentication
403 Authorization
Unexpected 500 before confirmed Commit
```

لأن الـstate قد تتغير والمحاولة التالية تحتاج إعادة تقييم.

لو مسار failure نفسه يعمل committed Business/Audit/Outbox effect مطلوبة، فلا يجوز Commit هذا الـeffect ثم إسقاط Idempotency outcome. الـEndpoint إما تحفظ النتيجة/المرجع ذريًا معها أو تعيد تصميم الـeffect كـbest-effort telemetry غير مؤثرة؛ لا نترك partial committed intent غير قابلة للمصالحة.

---

# 32. Business Rejection

Business rejection التي لم تعمل committed effect لا تتحول تلقائيًا إلى permanent stored result.

كل Endpoint تحدد semantics الخاصة بها لو ظهر سبب حقيقي مختلف.

---

# 33. Successful 201

Replay:

```text id="ri7t6t"
201
+
same created resource ID
+
same OperationId
```

ولا تنشئ Resource جديدة.

---

# 34. Successful 202

لو Command تنشئ Job طويلة:

```text id="20luxs"
First request
→ create one Job
→ return JobId

Replay
→ same JobId
→ same OperationId
```

Idempotency تحمي **إنشاء الـJob فقط**.

تنفيذ الـJob نفسه مسؤولية T21.

---

# 35. Default Guarantee Window

الـtechnical default للعمليات العامة:

```text id="3h1cdq"
24 hours
```

من `CompletedAtUtc` إلى `ReplayUntilUtc`.

لكنها:

```text id="6yxwyb"
Configurable / Domain-overridable
```

وليست Universal Business Rule.

Payments أو Provider workflows قد تحتاج مدة أطول.

Baseline Core V1:

```text
General required-idempotency operation
ReplayUntilUtc = CompletedAtUtc + 24 hours
PurgeAfterUtc  = ReplayUntilUtc + 7 days

RecordCashPayment / Refund / material financial posting
ReplayUntilUtc = CompletedAtUtc + 90 days
PurgeAfterUtc  = CompletedAtUtc + 180 days
```

والـfinancial Business `OperationId`/Payment uniqueness تظل أطول عمرًا حسب الـledger؛ حذف Idempotency record لا يحذفها ولا يسمح بتكرار Business operation نفسها.

---

# 36. Expired Key

قبل Expiry:

```text id="9xm6ls"
Replay allowed
```

عند/بعد `ReplayUntilUtc` وقبل `PurgeAfterUtc`:

```text id="of67zv"
409
Idempotency.KeyExpired
```

ولا نعد إن إعادة نفس intent بعد انتهاء guarantee window آمنة.

High-value flows قد تحتاج authoritative lookup قبل بدء Intent جديدة.

بعد `PurgeAfterUtc` يمكن للـcleanup حذف الـrecord، وتنتهي ضمانة HTTP Key التقنية. First-party clients ممنوعة من إعادة استخدام UUID قديمة، والعمليات عالية القيمة تظل محمية أيضًا بBusiness `OperationId` uniqueness.

---

# 37. Retention ≠ Audit

Idempotency Records:

```text id="h0bhtc"
Operational temporary state
```

وليست:

```text id="1is9ai"
Audit history
Financial history
Business ledger
```

والـcleanup يتبع operational/background-job decisions.

الـcleanup تحذف فقط:

```text
Completed records
with PurgeAfterUtc <= nowUtc
```

وتعمل batches صغيرة باستخدام worker claiming مناسب لـT21. لا تحذف business rows أو audit/ledger history، ولا تبدأ قبل `PurgeAfterUtc`.

---

# 38. Booking / Enrollment

الـBooking تحتاج:

```text id="kl1eeo"
T17 Idempotency
+
T16 Seat Concurrency
+
Business Constraints
+
T15 Local Transaction
```

مثال:

### نفس الطالب يعيد نفس HTTP intent

```text id="la2jsi"
Same Key
→ T17
→ no duplicate booking
```

### طالبان مختلفان يحاولان آخر مقعد

```text id="2hz06d"
Different Keys
→ T16
→ Seat controlling-row lock
```

ودي نقطتان منفصلتان تمامًا.

---

# 39. Payments / Refunds

`RecordCashPayment` والRefund/financial posting المادي تستخدم Required Idempotency في Core V1.

`RecordCashPayment` تحديدًا:

```text
StudentFinance owns the Idempotency record
↓
Idempotency record + Payment + CashMovement
commit in the one shared PostgreSQL transaction approved by T15
↓
OperationId and PaymentId remain stable across retries
```

نفرض داخل scope المناسبة:

```text
UNIQUE business OperationId for RecordCashPayment
UNIQUE BranchFinance source (StudentPayment, PaymentId)
```

لكن T17 لا تحسم:

```text id="lix0fj"
Payment provider key mapping
Webhook deduplication
Financial ledger / audit retention
Payment state machine
```

دي تتحدد في Financial/Integration decisions المتخصصة.

---

# 40. Imports

Import Job creation يمكن حمايتها بـIdempotency:

```text id="3ex50y"
same upload/intent retry
→ no second ImportJob
```

لكن ده لا يعني Idempotency record لكل Excel row تلقائيًا.

---

# 41. Offline Attendance

Offline Attendance **لا تستخدم generic HTTP `Idempotency-Key` باعتبارها البروتوكول الأساسي للsync**.

عندنا Business requirement مستقلة:

```text id="mqmclz"
Offline attendance operation
→ stable OperationId
```

الـOperationId تسافر مع الـoffline operation نفسها وتستخدم لمنع إعادة تطبيق نفس mutation بعد:

```text id="2bd6q6"
Reconnect
Retry
Duplicate sync batch
Client restart
```

---

# 42. Idempotency-Key vs Attendance OperationId

```text id="5vhhrg"
HTTP Idempotency-Key
→ identifies one online logical request intent
→ short/medium replay window
→ endpoint-specific

Attendance OperationId
→ identifies one durable offline mutation
→ survives offline queue/reconnect
→ part of sync protocol
```

ممكن HTTP sync request نفسها تكون retryable، لكن:

> correctness لكل attendance mutation تعتمد على `OperationId` الخاصة بها، وليس فقط Key للBatch HTTP.

---

# 43. Example Offline Sync

```text id="020lfm"
Offline device
↓
Record attendance mutation
OperationId = O1
↓
Sync batch sent
↓
Server applies O1
↓
Response lost
↓
Client resends batch
↓
Server sees O1 already applied
↓
Does not apply attendance twice
```

تفاصيل conflict resolution والـsync protocol خارج T17.

---

# 44. Webhooks

Incoming Webhooks عادة تستخدم:

```text id="yagz1q"
Provider Event ID
+
Signature Verification
```

للDeduplication.

لا نفترض أن Provider سترسل `Idempotency-Key`.

Outgoing webhook retries تتبع T19/Integration decisions.

---

# 45. Internal Module Calls

لا نمرر Raw HTTP:

```text id="48acpx"
Idempotency-Key
```

عبر Modules كـBusiness Contract افتراضيًا.

لو internal workflow تحتاج operation identity مستقلة، تتصمم في T18/T19.

---

# 46. Error Contract

لEndpoint required-idempotency:

```text id="7sfx38"
Missing Key
→ 400 Idempotency.KeyRequired

Invalid Key
→ 400 Idempotency.KeyInvalid

Same Key + Different Intent
→ 409 Idempotency.KeyReuseMismatch

Same OperationId + Different Intent inside the same Trusted Scope
→ 409 Idempotency.OperationIdReuseMismatch

Still Processing
→ 409 Idempotency.RequestInProgress
→ Retry-After when appropriate

Expired Guarantee
→ 409 Idempotency.KeyExpired

Authoritative Commit outcome unavailable
→ 503 Idempotency.OutcomeUnknown
```

الـProblemDetails النهائية تتبع T32.

---

# 47. Testing

أي Endpoint Required Idempotency تختبر على PostgreSQL الحقيقية:

```text id="4tbswy"
First execution
Sequential replay
Concurrent same-key race
Key reuse mismatch
Rollback
Commit/response-lost scenario
Authorization changed before replay
Expiry
Unique constraint arbitration
Concurrent winner commits
Concurrent winner rolls back
Concurrent loser reaches lock_timeout
Global/no-institution scope still arbitrates exactly one winner
Same OperationId with a different Key
Fingerprint golden vectors and contract-version compatibility
Result-contract version compatibility
Completed replay after original Step-up ticket expiry
No completed record still requires fresh Step-up
ReplayUntil/PurgeAfter boundary
Authoritative-primary commit reconciliation
RecordCashPayment commits one Idempotency record + Payment + CashMovement
```

باستخدام:

```text id="a8q8ry"
Separate scopes
Separate DbContexts
Deterministic synchronization
```

وليس `Sleep`.

---

# القواعد النهائية

1. Idempotency انتقائية وليست Global.
2. Key واحدة لكل logical intent.
3. Retry لنفس intent تستخدم نفس Key.
4. الـClient تستخدم Key جديدة للintent الجديدة؛ نفس OperationId لا تصبح effect جديدة لمجرد تغيير Key.
5. Scope تشمل Operation + trusted caller + Institution عند الحاجة.
6. Same Key مع payload مختلفة = 409.
7. Semantic fingerprint إلزامية.
8. Raw request لا تخزن.
9. Raw Idempotency-Key لا تخزن أو تسجل.
10. Persistence تكون Module-local.
11. Unique Constraint تحسم same-key races.
12. Claim + Business Effect + Result تكون نفس owning transaction، بما فيها استثناء `RecordCashPayment` المعتمد.
13. Current Authentication/Authorization تعاد قبل replay.
14. لا Replay للSecrets.
15. لا Generic Idempotency لـLogin/Refresh/OTP/Recovery/Step-up أو Ticket issuance.
16. Idempotency لا تستبدل Concurrency.
17. Idempotency لا تستبدل Business Uniqueness.
18. Idempotency لا تستبدل Transactions.
19. Commit ambiguity تحل عبر same-key lookup وليس blind retry.
20. Default guarantee window = 24h قابلة للoverride.
21. Offline Attendance تعتمد durable OperationId خاصة بالsync.
22. Integration tests تستخدم PostgreSQL الحقيقية.
23. كل Required operation تملك Stable OperationId منفصلة عن Idempotency-Key.
24. Trusted scope تحسم بـVersioned non-null hash؛ nullable diagnostic columns لا تدخل Unique arbitration.
25. Fingerprint canonicalization وResult contract كلاهما Versioned ومغطى Golden tests.
26. Same-key claiming تستخدم `ON CONFLICT DO NOTHING RETURNING` داخل transaction المالكة.
27. Committed synchronous records تكون Completed؛ Processing غير المنفذة لا تحفظ منفصلة.
28. Replay تعيد Current Authorization، لكنها لا تطلب Fresh Step-up لإثبات عملية Completed بالفعل.
29. First execution بدون Completed record تطلب كل assurance الحالية، بما فيها Step-up عند الحاجة.
30. General replay window = 24h مع 7-day tombstone retention baseline.
31. Core V1 financial replay window = 90 days وPurgeAfter = 180 days، مع Business OperationId uniqueness أطول عمرًا.
32. Commit reconciliation تستخدم Authoritative primary فقط.
33. `RecordCashPayment` Idempotency record مملوكة لـStudentFinance وتدخل shared transaction المعتمدة في T15.
34. كل Required endpoint تعلن OperationId source وfingerprint/result versions وretention وStep-up requirement في policy واحدة.
35. Claim uniqueness تكون `NOT DEFERRABLE`، والـhashes وCompleted timeline/result invariants محمية بقيود Database.

---

# خارج نطاق T17

```text id="76cjq3"
Seat concurrency
→ T16

Cross-module contracts
→ T18

Events / Outbox / Inbox
→ T19

Offline attendance conflict resolution
Batch sync protocol
→ SessionsAttendance sync decision

Cleanup worker implementation / batching schedule
→ T21

Payment provider idempotency
Webhook signing/dedup
→ Financial / Integration decisions

ProblemDetails format
→ T32
```

---

# القرار النهائي المختصر

> EduCenterOS تستخدم **Selective Idempotency** للـmutation endpoints التي قد يؤدي retry لها إلى Business Effect مكرر. الـClient ترسل `Idempotency-Key` واحدة لكل logical intent وتعيد نفس الـKey عند retry لنفس العملية.

> الـServer تربط الـKey بـOperation + versioned non-null TrustedScopeHash، وتستخدم versioned semantic fingerprint وStable OperationId لمنع إعادة نفس Key أو OperationId مع intent مختلفة. Claim تتم بـ`ON CONFLICT DO NOTHING RETURNING`، والـrecord والـbusiness mutation والنتيجة تلتزم ذريًا في transaction المالكة.

> Completed result لا تعاد إلا بعد إعادة Authentication/Authorization الحالية، لكنها لا تحتاج Fresh Step-up لإثبات عملية تمت بالفعل. لا نخزن أو نعيد Credentials أو OTPs أو Refresh Tokens أو Secrets، ونخزن Result reference/payload صغيرة Versioned بدل Raw HTTP response.

> الـgeneral replay window هي 24 ساعة ثم tombstone لمدة 7 أيام، بينما العمليات المالية الأساسية تستخدم 90 يومًا وPurgeAfter عند 180 يومًا، مع Business OperationId uniqueness أطول عمرًا.

> Idempotency تحل **same intent repeated**؛ T16 تحل **different intents competing over the same state**. `RecordCashPayment` تحفظ StudentFinance idempotency record مع Payment وCashMovement داخل استثناء T15 الذري. وبالنسبة لـOffline Attendance، كل mutation تحمل durable `OperationId` داخل sync protocol؛ لا نعتمد على HTTP `Idempotency-Key` وحدها لمنع إعادة تطبيق العملية بعد reconnect أو batch retry.
