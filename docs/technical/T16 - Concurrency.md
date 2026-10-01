# T16 - Concurrency

## القرار

EduCenterOS يعتمد:

```text
Optimistic Concurrency by default
+
Application-managed int Version
+
Database Constraints
+
Pessimistic Row Locking for documented high-risk invariants
+
Read Committed by default
+
No blind retries
+
No global provider retry strategy for writes
+
Bounded lock / statement / transaction timeouts
+
Deterministic PostgreSQL race tests
```

والأداة تُختار حسب الـInvariant، وليس حسب الأسهل في EF Core.

---

# 1. المشاكل التي نحمي منها

T16 تحمي من:

```text
Lost Update
Race Conditions
Double Consumption
Stale State Decisions
Multi-row / Predicate Races
Concurrent State Transitions
```

أمثلة:

```text
Two users reserve the last seat
Two requests consume the same refresh credential
Two admins edit the same membership
Two requests consume the same verification proof
Two requests change enrollment capacity concurrently
```

---

# 2. Optimistic Concurrency هي Default

معظم الـBusiness state تستخدم:

```text
int Version
```

تبدأ:

```text
Version = 1
```

وتزيد عند كل mutation لها معنى Concurrency:

```text
Version++
```

وفي EF Core:

```csharp
builder.Property(x => x.Version)
    .IsConcurrencyToken()
    .IsRequired();
```

## 2.1 Version Increment Discipline

`Version` Application-managed، ولذلك لا يكفي تعليمها كـConcurrency Token فقط.

القواعد:

- الـAggregate/Entity تبدأ بـ`Version = 1` عند الإنشاء.
- Domain mutation ذات معنى Concurrency ترفعها مرة واحدة قبل الحفظ.
- تغيير Child يؤثر على قرار Aggregate لاحق يرفع Root version أيضًا.
- Infrastructure validation/`SaveChanges` interceptor تتحقق أن أي tracked update محمية غيّرت Version كما هو متوقع، وتفشل التطوير/الاختبار لو نسيها المسار.
- لا يرفع Interceptor الـVersion تلقائيًا لكل property change؛ قرار معنى التغيير يظل للDomain.
- `ExecuteUpdate` وRaw SQL مسؤولان صراحة عن `Version = Version + 1` وفحص affected rows.

---

# 3. Version ليست لكل Entity

لا نضيف `Version` لكل Table تلقائيًا.

نستخدمها عندما:

- نفس state قد تعدل بالتزامن.
- Lost update لها أثر Business.
- State transitions متنافسة ممكنة.
- User قد يحفظ تعديلًا مبنيًا على snapshot قديمة.

مرشحة مثل:

```text
UserAccount
UserSession
RefreshTokenRecord
OtpChallenge
InstitutionMembership
GroupSeatInventory / Enrollment Capacity state
StudentEnrollment
Financial workflow state
```

حسب كل Feature.

---

# 4. Version ≠ SecurityVersion

```text
Version
→ Concurrency

SecurityVersion
→ Credential/session invalidation
```

مثال:

```text
Membership edited
→ Version++

Password reset
→ Version++
→ SecurityVersion++
```

حسب Domain rules.

ولا نستخدم:

```text
UpdatedAtUtc
SecurityVersion
xmin
```

كـUniversal concurrency token.

---

# 5. Lost Update Handling

لو:

```text
Request A reads Version 5
Request B reads Version 5

A saves → Version 6
B attempts save using original Version 5
```

B تحصل على:

```text
DbUpdateConcurrencyException
```

وده لا يعني تلقائيًا:

```text
return 409
```

لازم الـUse Case تصنف المعنى.

---

# 6. Conflict Classification

بعد Conflict عندنا حالات مختلفة.

## Stale User Decision

مثل Admin يعدل record بعد تعديل شخص آخر:

```text
→ 409 Conflict
```

## Objective Already Satisfied

مثل Logout لجلسة اتلغت بالفعل:

```text
→ semantic success
```

لو Contract العملية تسمح.

## Safe Recompute

مثل counter/security operation قابلة لإعادة التقييم:

```text
Re-read
→ re-evaluate
→ retry
```

## Security-sensitive Race

مثل Refresh credential consumption:

```text
Re-read authoritative state
→ apply security policy
→ fail closed / revoke as required
```

## Unknown Infrastructure Failure

لا نحولها إلى Business Conflict مزيفة.

---

# 7. ممنوع Blind Retry

ممنوع:

```text
Save
↓ conflict
Reload version
↓
Save same mutation again
```

الـRetry الصحيحة:

```text
Rollback failed attempt
↓
Discard stale tracked state
↓
Re-read authoritative state
↓
Re-run relevant rules
↓
Re-apply original intent
↓
Try again
```

الأفضل في Automatic retry إنشاء Application scope و`DbContext` جديدة لكل محاولة. `ChangeTracker.Clear()` مسموحة فقط داخل retry implementation مدروسة عندما ثبت أنها تعيد بناء كل state المطلوبة؛ ليست البديل الافتراضي عن Context جديدة.

وعند الحاجة في مسار مدروس:

```csharp
dbContext.ChangeTracker.Clear();
```

---

# 8. Retry Budget

Automatic concurrency retry ليست عامة.

لو Use Case مثبت إنها replay-safe:

```text
Maximum total attempts = 3
```

أي:

```text
1 initial attempt
+
maximum 2 retries
```

لكن stale interactive edits غالبًا:

```text
0 automatic retries
→ 409
```

## 8.1 Retry Classification Matrix

```text
DbUpdateConcurrencyException
→ Use-case classification; usually 409 or full safe recompute

40P01 deadlock_detected
→ whole-transaction retry only when replay-safe and rollback confirmed

40001 serialization_failure
→ whole-transaction retry only when replay-safe and rollback confirmed

23505 unique_violation
→ map by known constraint; no retry

55P03 lock_not_available / lock timeout
→ no automatic retry for interactive commands by default

57014 query_canceled
→ no automatic write retry

25P04 transaction_timeout
→ transaction aborted; no automatic write retry by default

40003 statement_completion_unknown
08007 transaction_resolution_unknown
connection loss during/after commit
→ unknown outcome; T17 reconciliation, never blind retry
```

لا نصنف باستخدام نص رسالة Exception؛ نستخدم `SQLSTATE` واسم الـConstraint عندما توفر.

## 8.2 Retry Execution Policy

عند السماح صراحة بـRetry:

```text
Maximum total attempts = 3
New scope + new DbContext per attempt
Re-run the whole transaction delegate
Re-read authoritative state
Re-run authorization/business rules that may have changed
Preserve the same OperationId
```

الـbackoff baseline بين المحاولات:

```text
Exponential backoff with jitter
Initial delay = 25 ms
Maximum delay = 250 ms
```

القيم Infrastructure Configuration قابلة للضبط، وليست Business constants.

في Core V1:

```text
Global EnableRetryOnFailure = disabled
```

لأن provider retry العامة قد تعيد Network/Transient failures لا نعرف معها نتيجة Commit، ولأن Explicit Transactions تحتاج execution strategy تلف الـdelegate كاملة. أي Read-only retry أو Feature-specific strategy تحتاج تسجيلًا صريحًا ولا توسع Write retry policy ضمنيًا.

---

# 9. Minimal Mechanism Principle

نستخدم أبسط آلية تحقق الـInvariant بصورة صحيحة.

تقريبًا:

```text
Constraint
↓
Optimistic Version
↓
Atomic SQL
↓
Controlling-row lock
↓
Stronger isolation
```

مش ترتيب إلزامي، لكن ممنوع القفز إلى Locks/Serializable بدون داعٍ.

---

# 10. Database Constraints

الـDatabase تظل الحكم النهائي في Invariants القابلة للتعبير كConstraint.

مثل:

```text
Unique normalized phone
Unique normalized email whenever present, even before verification
Unique business code
Unique token hash
```

Pattern:

```text
Friendly pre-check
+
Database constraint
+
Known violation mapping
```

ولا نعتمد على:

```text
SELECT exists
→ INSERT
```

وحدها.

---

# 11. Pessimistic Row Locking

نستخدمها فقط عندما نحتاج:

> أحدث authoritative state قبل اتخاذ قرار لا يجوز أن يحدث بالتوازي.

الأداة الأساسية في PostgreSQL:

```sql
SELECT ...
FOR UPDATE
```

وتستخدم فقط داخل:

```text
Short explicit owning transaction
or the one approved T15 same-database transaction
```

---

# 12. FOR UPDATE مناسبة عندما

```text
One local row or a small bounded set of owning rows controls the invariant
+
Decision must serialize
+
Operation is short
+
No external calls
```

ولا نستخدمها:

```text
For every write
For GET queries
Across Modules
Around network calls
Across user interaction
On large sets by default
```

---

# 13. Seat Capacity — High-risk Invariant

الـhard local invariant:

```text
OccupiedCapacity
<=
EnrollmentCapacity
<=
MaxAllowedEnrollmentCapacitySnapshot
```

حيث:

```text
OccupiedCapacity
=
Confirmed Enrollments
+
Active Seat Reservations
+
Any other state explicitly counted by policy
```

بعد T15:

```text
EnrollmentCapacity
+
Seat Reservations
+
Confirmed Enrollments
```

كلهم تحت ownership:

```text
Enrollments
```

---

# 14. Seat Controlling Row

كل Group قابلة للتسجيل يكون لها controlling state داخل `Enrollments`.

مفهوميًا:

```text
GroupSeatInventory
or
GroupEnrollmentPolicy
```

تحمل على الأقل:

```text
GroupId
EnrollmentCapacity
MaxAllowedEnrollmentCapacitySnapshot
AcademicConstraintVersion
ConstraintStatus
Version
```

الاسم والـschema التفصيلية تتحدد في تنفيذ Enrollments.

الحد الأدنى لسلوك `ConstraintStatus`:

```text
Active
→ seat-consuming mutations allowed

PendingInitialConstraint / Suspended / unavailable state
→ no new seat consumption
```

لا تصبح Group قابلة للحجز قبل وجود Active accepted constraint snapshot، ويكون `GroupId` فريدًا داخل Enrollments حتى توجد controlling row واحدة فقط لكل Group.

---

# 15. قاعدة إلزامية لكل Seat Mutation

أي عملية يمكن أن تغير:

```text
OccupiedCapacity
or
EnrollmentCapacity
or
MaxAllowedEnrollmentCapacitySnapshot / AcademicConstraintVersion
```

يجب أن تمر على **نفس controlling row**.

مثل:

```text
Create SeatReservation
Confirm direct Enrollment
Release Reservation
Cancel Enrollment when seat becomes free
Expire Reservation
Transfer into group
Change Enrollment Capacity
Apply Academic Constraint Change
Waitlist Offer → Reservation
```

---

# 16. Reserve Last Seat

الـpattern المعتمدة:

```text
Begin Enrollments transaction
↓
SELECT GroupSeatInventory
FOR UPDATE
↓
Read authoritative counted occupancy
↓
Check:

ConstraintStatus = Active

and

OccupiedCapacity < EnrollmentCapacity

and

EnrollmentCapacity <= MaxAllowedEnrollmentCapacitySnapshot

↓
Create SeatReservation
↓
Save
↓
Commit
```

وبالتالي Request ثانية لنفس Group تنتظر نفس controlling row.

---

# 17. مثال Race على آخر مقعد

نفترض:

```text
Capacity = 20
Occupied = 19
```

Request A وB يحاولوا الحجز.

## A

```text
Lock SeatInventory
↓
Occupied = 19
↓
Reserve
↓
Occupied becomes effectively 20
↓
Commit
```

## B

تنتظر الـlock.

بعد A:

```text
B acquires lock
↓
Re-read authoritative occupancy
↓
Occupied = 20
↓
No seat available
```

النتيجة:

```text
A ✅
B ❌ SeatUnavailable
```

وليس:

```text
21 / 20
```

---

# 18. لماذا مش Optimistic Version وحدها؟

لو الـoccupancy ناتجة من عدة Rows:

```text
Reservations
+
Enrollments
```

فـVersion على Reservation واحدة لا تحمي الـPredicate:

```text
count(occupied) < capacity
```

الـcontrolling-row lock تعمل Serialization لكل Seat-affecting commands حول نفس Group.

---

# 19. قاعدة مهمة جدًا

الـSeat locking protocol ينجح فقط إذا:

> **كل path تغيّر حالة محسوبة في OccupiedCapacity تحصل على نفس controlling-row lock أولًا.**

ممنوع Feature جديدة تعمل:

```text
Insert enrollment directly
```

بدون المرور بنفس concurrency protocol.

---

# 20. Change Enrollment Capacity

تغيير Capacity يستخدم نفس controlling row:

```text
Begin transaction
↓
Lock GroupSeatInventory
↓
Calculate authoritative OccupiedCapacity
↓
Validate:

NewCapacity >= OccupiedCapacity

and

NewCapacity <= MaxAllowedEnrollmentCapacitySnapshot

↓
Apply change
↓
Version++
↓
Commit
```

وبالتالي لا يمكن بالتزامن:

```text
Admin reduces capacity
+
Student reserves seat
```

بناءً على snapshots مختلفة.

---

# 21. Academic Physical Constraint

وقت Seat mutation لا تعتمد Enrollments على Live Academic read قابلة للتقادم؛ تعتمد على النسخة المقبولة محليًا داخل `GroupSeatInventory`.

ولا نعمل:

```text
Enrollments transaction
→ lock Academic row
```

## 21.1 Common Version Rules

كل تغيير يحمل:

```text
ConstraintChangeId
GroupId
ProposedLimit
AcademicConstraintVersion
Direction / activation fact
```

بعد Lock الـ`GroupSeatInventory`:

- نفس `ConstraintChangeId` ونفس payload تعيد نفس النتيجة Semantic/idempotent.
- Version مساوية للحالية مع Change مختلفة أو payload مختلفة تفشل Conflict.
- Version أقدم لا تعيد state للخلف.
- Version بها gap لا تطبق تخمينًا؛ تطلب resync/reconciliation من Contract المملوكة حسب T18.
- الانتقال المقبول يرفع `GroupSeatInventory.Version` أيضًا.

## 21.2 Lower Limit / Stricter Constraint

```text
Academic proposes next constraint version
↓
Enrollments begins local transaction
↓
Lock GroupSeatInventory
↓
Re-read authoritative OccupiedCapacity
↓
If OccupiedCapacity > ProposedLimit → reject
Else persist the lower MaxAllowedEnrollmentCapacitySnapshot
and lower EnrollmentCapacity when needed
↓
Commit accepted version
↓
Academic activates the stricter change
```

Seat reservation والتخفيض يتنافسان على نفس row؛ إما الحجز يسبق فيُرفض التخفيض إن كسر الإشغال، أو التخفيض يسبق فيرى الحجز الحد الجديد.

## 21.3 Raise Limit / Wider Constraint

```text
Academic activates the higher limit first
↓
Enrollments receives versioned activation
↓
Begin local transaction
↓
Lock GroupSeatInventory
↓
Validate exact next version / idempotency
↓
Raise MaxAllowedEnrollmentCapacitySnapshot
↓
Commit
```

رفع الحد الأقصى لا يرفع `EnrollmentCapacity` تلقائيًا. لو فشل تحديث Enrollments تظل على الحد الأقدم الأشد، ولا يمكن استخدامها للسعة الأعلى قبل تفعيل Academic.

## 21.4 Initial Constraint Activation

```text
Create GroupSeatInventory as PendingInitialConstraint
with no seat consumption allowed
↓
Receive the first active Academic constraint/version
↓
Lock GroupSeatInventory
↓
Set MaxAllowedEnrollmentCapacitySnapshot
Set EnrollmentCapacity <= accepted maximum
Set ConstraintStatus = Active
Version++
↓
Commit
```

Concurrent duplicate initialization تعيد نفس النتيجة، وأي payload/version مختلفة تفشل Conflict أو تدخل resync بدل إنشاء controlling row ثانية.

---

# 22. Reservation → Enrollment

لو Active Reservation **تستهلك المقعد بالفعل**:

```text
Reservation counted = 1
Enrollment counted = 1
```

فالتحويل:

```text
Reservation
→ Confirmed Enrollment
```

لا يجب أن يستهلك مقعدًا ثانيًا.

الـDomain تحسب Occupancy حسب **counted states** وليس عدد Rows الخام.

---

# 23. Waitlist

Waitlist لا تستهلك Capacity.

لذلك:

```text
Join Waitlist
```

لا تحتاج Seat Lock لمجرد إضافتها للقائمة، إلا لو نفس operation تحاول تحويلها إلى Reservation/Enrollment.

لكن:

```text
Waitlist Offer
→ Reserve Seat
```

تدخل نفس Seat concurrency protocol.

---

# 24. Reservation Expiry

انتهاء الوقت منطقيًا لا يحتاج Background update كي تصبح Reservation expired.

لكن لو تحرير المقعد يتطلب state transition persisted أو worker:

> أي mutation تؤثر على occupancy تمر على نفس controlling row.

تفاصيل الـBackground Job تتبع T21.

---

# 25. Lock Ordering

لو Transaction تحتاج أكثر من controlling row:

```text
Transfer between groups
```

يجب الحصول على Locks بترتيب ثابت.

مثل:

```text
Order by GroupId ascending
```

ثم:

```text
Lock source
Lock target
```

بنفس الترتيب في كل code paths.

وده يقلل Deadlocks.

الـlock order ليست convention شفوية؛ كل lock-based Feature توثق ترتيبها وتغطيه Integration Tests. الـorders المعتمدة في Core V1:

```text
Any T17 Required-idempotency flow
→ Win the module-owned idempotency claim before acquiring business-state locks

Enrollments transfer
→ GroupSeatInventory ordered by GroupId ascending

BranchFinance cash flows touching both
→ CashDrawer first, then CashShift

IdentityAccess state transitions
→ UserAccount, then UserSession ordered by Id,
  then credential/proof rows ordered by Id
```

---

# 26. Transfer Example

Transfer يمكن أن تحتاج حماية:

```text
Source Group Seat State
+
Target Group Seat State
```

فنستخدم:

```text
Sort GroupIds
↓
Lock both seat rows in deterministic order
↓
Validate target capacity
↓
Apply source/target transitions
↓
Commit
```

داخل `EnrollmentsDbContext` فقط.

---

# 27. NOWAIT / SKIP LOCKED

ليست Default.

```text
NOWAIT
```

تستخدم فقط لو Fast Failure جزء مقصود من Contract.

```text
SKIP LOCKED
```

لا تستخدم لحماية correctness لطلبات التسجيل.

قد تستخدم مستقبلًا في Background Job claiming حسب T21.

---

# 28. Advisory / Distributed Locks

غير معتمدة كـdefault.

لا نستخدم:

```text
PostgreSQL advisory locks
Redis locks
Process Semaphore
Static Mutex
```

لحماية Seat أو Business DB invariants العادية.

---

# 29. Read Committed

تظل الـDefault:

```text
Read Committed
```

ومع Seat protocol:

```text
Read Committed
+
Controlling-row FOR UPDATE
+
Local transaction
```

تكفي للـserialization المطلوبة طالما كل seat-changing paths تلتزم بنفس protocol.

لا نرفع إلى Serializable لمجرد Seat Booking.

---

# 30. Serializable

تستخدم فقط لو ظهرت Invariant حقيقية لا يمكن تمثيلها بأداة أبسط.

تحتاج:

```text
Documented reason
Short transaction
PostgreSQL concurrent test
Whole-transaction retry strategy
```

---

# 31. Deadlocks

PostgreSQL Deadlock:

```text
SQLSTATE 40P01
```

ولو Use Case replay-safe:

```text
Rollback confirmed
↓
Re-run the whole approved transaction boundary
```

وليس statement واحدة فقط.

تتبع المحاولة budget/backoff في القسم 8، وتستخدم scope/DbContext جديدة ونفس `OperationId`.

---

# 32. Serialization Failure

```text
SQLSTATE 40001
```

تتعامل بنفس المبدأ:

```text
Retry whole transaction only
```

وبشكل bounded عند السماح.

تتبع نفس policy في القسم 8؛ لا نسمح لـprovider strategy مستقلة بإعادة statement أو transaction خارجها.

---

# 33. Commit Ambiguity

لو غير معروف هل Commit نجحت:

```text
Do not retry blindly
```

ده T17 Idempotency / reconciliation problem.

يشمل ذلك صراحة:

```text
SQLSTATE 40003
SQLSTATE 08007
connection loss while Commit outcome is unknown
```

## 33.1 Lock and Transaction Timeouts

لا نترك Interactive lock-based commands تنتظر بلا حد. Baseline Core V1 داخل الـExplicit Transaction:

```text
SET LOCAL lock_timeout = '3s'
SET LOCAL statement_timeout = '15s'
SET LOCAL transaction_timeout = '20s'
```

القواعد:

- القيم مركزية في Infrastructure Configuration ويمكن ضبطها بعد القياس.
- نستخدم `SET LOCAL` حتى تنتهي مع الـTransaction، ولا نغير PostgreSQL global defaults من التطبيق.
- `lock_timeout` أقصر من `statement_timeout`، و`statement_timeout` أقصر من `transaction_timeout`.
- Background/batch profiles قد تملك قيمًا منفصلة موثقة، ولا ترث Interactive profile عشوائيًا.
- Request cancellation لا تثبت rollback لو حدثت أثناء Commit؛ نطبق T15/T17.
- `55P03`/lock timeout لا تتحول إلى fake stale `409`؛ HTTP mapping النهائية في T32، والـdefault المتوقع transient/busy response.
- `57014` الناتجة عن timeout/cancellation لا تعاد كـwrite تلقائيًا.

---

# 34. Atomic SQL

لو invariant بسيطة يمكن حمايتها في statement واحدة:

```text
UPDATE ...
WHERE predicate
```

مع:

```text
affected rows check
```

فده Pattern مسموح ومفضل أحيانًا.

لكن لا نستخدم SQL trick معقدة بدل Domain model واضحة لو القاعدة مركبة.

---

# 35. ExecuteUpdate / ExecuteDelete

لا تحصل تلقائيًا على tracked EF optimistic concurrency.

لو تستخدم على state محمية:

```text
WHERE Id = @id
AND Version = @expectedVersion
```

مع:

```text
Version = Version + 1
```

وفحص affected rows.

---

# 36. Raw SQL

أي Raw SQL mutation يجب أن تحافظ على:

```text
Tenant predicate
Concurrency predicate / lock
Version semantics
Affected-row verification
```

ولا تستخدم على Table تملكها Module أخرى.

---

# 37. Client-visible Version

وجود `Version` داخليًا لا يعني إظهارها في كل API.

لكن لو UX تسمح Long-lived editing ويمكن أن نرفض overwrite:

```text
Response
→ Version

Update
→ ExpectedVersion
```

وتستخدم فقط كـprecondition.

Client لا تحدد:

```text
NextVersion
```

---

# 38. ETag

لا يوجد:

```text
Global ETag / If-Match standard
```

في V1.

يمكن اتخاذ قرار لاحقًا لو احتجناه على نطاق واسع.

---

# 39. 409 Conflict

Genuine stale-state conflict:

```text
409
```

لكن:

```text
Cross-tenant hidden resource
→ 404

Authentication/security failure
→ appropriate 401/403

Unknown infrastructure failure
→ not fake 409
```

---

# 40. Tenant Isolation

كل concurrency-aware update تظل خاضعة لـ:

```text
InstitutionId
Tenant filters
Write guards
Authorization
```

لا نزيل Tenant predicate لحل Conflict.

---

# 41. Cross-Module Locks

ممنوع:

```text
Module A
→ FOR UPDATE Module B table
```

أو:

```text
Shared database
→ shared lock ownership
```

كل Module تقفل state التي تملكها فقط.

لو invariant لا يمكن حمايتها بدون cross-module lock:

> نراجع ownership/design أولًا، ثم نستخدم named same-database exception فقط لو كانت معتمدة في T15.

`RecordCashPayment` لا تعطي StudentFinance حق Lock أو قراءة جداول BranchFinance والعكس. الـorchestrator تجمع participant من كل Module داخل الـshared transaction، وكل participant تحصل على Locks وتكتب state التي تملكها فقط.

---

# 42. Process-local Locks

ممنوع الاعتماد على:

```csharp
lock (...)
SemaphoreSlim
```

لحماية Database invariant.

التصميم يجب أن يظل صحيحًا مع عدة API instances.

---

# 43. Security Races

IdentityAccess تستخدم بروتوكولات صريحة بدل الاعتماد على Optimistic Version وحدها.

## 43.1 IdentityAccess Lock Order

عندما تحتاج Transaction أكثر من row، الترتيب المعتمد:

```text
UserAccount
↓
UserSession rows ordered by Id
↓
Credential / Challenge / Proof rows ordered by Id
```

يجوز عمل lookup غير locking بالـhash لتحديد IDs، لكن بعد بدء Transaction يعاد تحميل/قفل authoritative rows بالترتيب ويعاد فحص كل الشروط.

## 43.2 Generic Single-use Proof Pattern

ينطبق على:

```text
OtpChallenge
VerificationProof
RecoveryCode
MfaLoginChallenge
StepUpGrant
```

الاستهلاك يتم بـconditional mutation أو row lock داخل نفس Transaction التي تنفذ النتيجة المملوكة لـIdentityAccess:

```text
UPDATE ...
SET consumed_at_utc = @now,
    version = version + 1
WHERE id = @id
  AND consumed_at_utc IS NULL
  AND revoked_at_utc IS NULL when applicable
  AND expires_at_utc > @now
  AND expected bindings match
RETURNING id
```

`affected rows = 0` تعني إعادة قراءة وتصنيف السبب، وليس إعادة الاستهلاك. لا ترسل Credential/Proof جديدة قبل Commit.

Failed OTP/MFA challenge attempts تستخدم atomic increment أو row lock على نفس Challenge حتى لا تضيع محاولات متزامنة، وتطبق max-attempt transition في نفس statement/transaction.

## 43.2.1 TOTP Replay Protection

بعد التحقق cryptographically من الكود، تقفل العملية `MfaMethod` الفعالة أو تستخدم conditional update:

```text
UPDATE mfa_methods
SET last_accepted_time_step = @candidateStep,
    version = version + 1
WHERE id = @methodId
  AND status = Active
  AND (last_accepted_time_step IS NULL
       OR last_accepted_time_step < @candidateStep)
```

وتنشئ Login/Step-up evidence المطلوبة في نفس Transaction. بذلك نفس TOTP time-step تنجح مرة واحدة فقط حتى مع Requests متزامنة؛ `affected rows = 0` تفشل Replay ولا تعاد تلقائيًا.

## 43.3 Refresh Rotation and Reuse

الـflow المعتمدة:

```text
Resolve refresh hash to candidate IDs
↓
Begin IdentityAccess transaction
↓
Lock UserAccount
↓
Lock UserSession
↓
Lock current RefreshTokenRecord
↓
Re-check account/session/security version/expiry
↓
If already consumed → apply T13 reuse policy and revoke session
Else consume current token + create one replacement + update session activity
↓
Commit
```

طلبان لنفس Refresh لا ينتجان فرعين. في Core V1 بدون grace period قد ينجح الدوران الأول ثم ترى المحاولة المتزامنة الـcredential مستهلكة وتبطل الـSession حسب T13؛ النتيجة الأمنية المقصودة ألا يبقى أكثر من descendant صالح.

## 43.4 Login vs Security State Change

Password hashing verification الثقيلة تحدث خارج open transaction. قبل إصدار Session:

```text
Begin transaction
↓
Lock UserAccount
↓
Verify the password hash/security snapshot used is still current
↓
Re-check account status / lockout
↓
Reset failure state, optional approved rehash, create session
↓
Commit
```

لو الـhash/security snapshot تغيرت، لا تعتمد نتيجة verification القديمة؛ تعاد العملية من authoritative state بميزانية bounded مستقلة أو تفشل بأمان.

Suspension/Closure/Password reset/MFA security reset تقفل `UserAccount` أولًا، تزيد `SecurityVersion` عند القاعدة المحددة في T12/T13، ثم تقفل/تبطل Sessions بترتيب Id. بذلك إما Login تكتمل أولًا ثم يبطلها التغيير، أو التغيير يسبق فترفض Login.

تحديث failed-login counters يستخدم atomic update أو Account row lock؛ ممنوع read-modify-write غير محمي يفقد محاولات متزامنة.

## 43.5 StepUpGrant → OperationAuthorizationTicket

داخل IdentityAccess transaction واحدة:

```text
Lock/conditionally consume exact StepUpGrant
↓
Create one OperationAuthorizationTicket
↓
UNIQUE(operation_authorization_tickets.step_up_grant_id)
↓
Commit
```

في Target Module لا نحاول قفل IdentityAccess. بعد validation وقبل mutation، تحفظ Target Module `TicketId + OperationId` في نفس local transaction وتفرض:

```text
UNIQUE(TicketId)
UNIQUE(OperationId) within the operation scope defined by T17
```

Concurrent delivery لنفس intent تنتج Business effect واحدة؛ الـrequest الأخرى تعيد نفس النتيجة عبر T17 أو تصنف InProgress، ولا تنفذ intent ثانية.

---

# 44. Constraint Race

Create operations التي لها uniqueness تستخدم:

```text
Friendly pre-check
+
DB unique constraint
```

والـDB هي الحكم تحت Race.

PostgreSQL:

```text
23505
→ unique_violation
```

ولا نخلطها تلقائيًا مع `DbUpdateConcurrencyException`.

---

# 45. Financial State

T16 لا تحسم Financial Ledger architecture كلها، لكنها تحسم Concurrency لاستثناء `RecordCashPayment` المعتمد في T15.

## 45.1 RecordCashPayment Lock Protocol

```text
Resolve actor / Step-up ticket / caller-supplied OperationId
and T17 key/scope/fingerprint before transaction
↓
Resolve candidate DrawerId/ShiftId without treating the pre-read as authoritative
↓
Open one shared PostgreSQL transaction per T15
↓
StudentFinance participant claims T17 idempotency record
using ON CONFLICT DO NOTHING RETURNING
↓
Only the winning claim continues to cash locks
↓
BranchFinance participant locks CashDrawer
↓
BranchFinance participant locks CashShift
↓
Re-check Institution/Branch/Drawer/Shift/cashier eligibility and Open status
↓
StudentFinance participant creates Payment with pre-generated PaymentId
↓
BranchFinance participant creates CashMovement referencing PaymentId
↓
Persist module-owned Outbox records
↓
Complete StudentFinance idempotency result
↓
Commit once
```

كل BranchFinance flow تمس الاثنين تتبع:

```text
CashDrawer → CashShift
```

وكل flow تغلق Shift تحصل على نفس Locks قبل تغييرها. النتيجة الخطية:

- لو Payment حصلت على Locks أولًا، تظهر حركتها قبل أن يكتمل الإغلاق.
- لو Close حصلت عليها أولًا، تعيد Payment الفحص وترفض `CashShiftClosed`.
- بعد قفل Shift يعاد التأكد أنها ما زالت مرتبطة بالـDrawer المرشحة؛ الـpre-read لا تثبت العلاقة.
- لا توجد Payment committed بدون CashMovement أو العكس.
- BranchFinance تفرض uniqueness على `(SourceType = StudentPayment, SourceId = PaymentId)` طبقًا لـT15.
- لا External calls ولا Provider calls داخل الـTransaction.
- Commit ambiguity تستخدم نفس `OperationId` وT17؛ لا تنشئ Payment جديدة.
- Same-key requests تتنافس على StudentFinance idempotency unique index قبل Cash locks؛ الخاسرة لا تنفذ Financial effect.

## 45.2 Other Financial State

أي mutable financial workflow state تستخدم حماية مناسبة من lost update، ولا نعتمد mutable `Balance` كSource of Truth بدون القرار المالي المختص.

---

# 46. Deterministic Concurrency Tests

أي flow حساسة يجب اختبارها على PostgreSQL الحقيقية باستخدام:

```text
Separate scopes
Separate DbContexts
Barrier
TaskCompletionSource
Controlled transaction coordination
```

وليس:

```text
Thread.Sleep
Task.Delay
Hope
```

---

# 47. Seat Race Test

اختبار إلزامي قبل إطلاق Seat Reservation:

```text
Capacity = 1
Occupied = 0

Start N concurrent reserve commands
```

ونثبت:

```text
Exactly one seat-consuming winner
No overbooking
All other results are allowed deterministic business conflicts
Final occupied capacity = 1
```

---

# 48. Capacity Reduction Race Test

مثال:

```text
Capacity = 2
Occupied = 1
```

بالتزامن:

```text
A → Reserve another seat
B → Reduce capacity to 1
```

الناتج يجب أن يمثل **ترتيبًا صحيحًا واحدًا**:

إما:

```text
B wins first
→ capacity 1
→ A fails
```

أو:

```text
A wins first
→ occupied 2
→ B fails
```

ممنوع:

```text
Capacity = 1
Occupied = 2
```

---

# 49. Transfer Race Tests

لو Transfer تقفل مجموعتين:

نختبر concurrent opposite transfers للتأكد من:

```text
No deadlock storm
Deterministic lock order
Target capacity preserved
No lost source history
```

## 49.1 Academic Constraint Race Tests

نغطي على الأقل:

```text
Seat reservation vs stricter-limit proposal
Enrollment-capacity change vs accepted academic limit
Duplicate ConstraintChangeId with same payload
Same ConstraintChangeId with different payload
Old academic version after a newer version
Version gap requiring resync
Higher limit cannot be used before Academic activation
Initial group cannot consume seats before Active constraint snapshot
```

في كل حالة تظل:

```text
OccupiedCapacity
<= EnrollmentCapacity
<= MaxAllowedEnrollmentCapacitySnapshot
```

## 49.2 Cash Payment Race Tests

```text
RecordCashPayment vs CloseCashShift
Two cash payments on the same shift
Failure after Payment SaveChanges and before CashMovement SaveChanges
Failure after both participant saves and before shared Commit
Duplicate PaymentId/source constraint
Deadlock retry keeps the same OperationId and creates one effect
Commit ambiguity does not create a second Payment
```

نثبت final state في StudentFinance وBranchFinance، وأن Payment/CashMovement إما الاثنان committed أو الاثنان absent.

## 49.3 IdentityAccess Race Tests

```text
Refresh vs same Refresh
Refresh vs Logout / LogoutAll / Security Reset
Successful Login vs Suspension / Password Reset
Concurrent failed logins preserve every increment
OTP / VerificationProof / RecoveryCode double consumption
Same TOTP time-step submitted concurrently succeeds once
Concurrent failed challenge attempts preserve every increment and limit
StepUpGrant concurrent ticket issuance creates one ticket
OperationAuthorizationTicket concurrent delivery creates one business effect
Same ticket with different OperationId/module/operation/resource fails closed
Target transaction failure allows only same-intent retry before ticket expiry
```

الاختبار لا يكتفي بعدد Responses؛ يفحص Sessions/Credentials/Tickets والـtarget business state النهائية.

---

# 50. Final DB State أهم من HTTP فقط

Concurrency test لا يكفي أن تقول:

```text
No 500
```

لازم نفحص:

```text
Final authoritative PostgreSQL state
+
Allowed operation outcomes
```

---

# القواعد النهائية

1. Optimistic Concurrency هي default.
2. `int Version` هي default token عند الحاجة.
3. Version تبدأ من 1.
4. Version تزيد مع concurrency-relevant mutation.
5. Version منفصلة عن SecurityVersion.
6. لا Blind Retry.
7. Retry تعيد القراءة والقواعد والـintent.
8. Automatic retries فقط للReplay-safe flows.
9. Maximum default budget عند السماح = 3 attempts.
10. DB constraints هي الحكم النهائي للUniqueness.
11. `FOR UPDATE` تستخدم فقط في short owning transaction أو استثناء T15 المعتمد.
12. Foreign cross-module locks ممنوعة؛ participant الاستثناء المعتمد في T15 تقفل rows مملوكة لها فقط.
13. Process/distributed locks ليست default.
14. Read Committed تظل default.
15. Serializable ليست default.
16. Seat invariant تستخدم controlling row داخل Enrollments.
17. كل Seat-affecting mutation تمر بنفس locking protocol.
18. Transfers تقفل seat rows بترتيب deterministic.
19. Commit ambiguity لا تعاد عشوائيًا.
20. Concurrency tests تستخدم PostgreSQL الحقيقية وبـdeterministic synchronization.
21. `MaxAllowedEnrollmentCapacitySnapshot` وAcademic version تحميان مع Seat state تحت نفس Enrollments lock.
22. خفض Academic limit يمر على Enrollments أولًا؛ رفعه يتفعل في Academic أولًا.
23. Global `EnableRetryOnFailure` معطلة للWrite flows في Core V1.
24. Retry المسموحة تستخدم scope/DbContext جديدة ونفس OperationId وwhole-transaction delegate.
25. Interactive lock-based transactions لها `SET LOCAL` timeouts مركزية ومحدودة.
26. IdentityAccess تتبع lock order ثابتة واستهلاك proof ذريًا.
27. StepUpGrant تصدر Ticket واحدة؛ Target Module تنفذ Business effect واحدة لكل Ticket/OperationId.
28. `RecordCashPayment` تتبع `CashDrawer → CashShift` داخل الاستثناء الذري المعتمد.
29. أي T17 Required flow تحسم module-owned idempotency claim قبل الحصول على business-state locks.

---

# خارج نطاق T16

T16 لا تحسم:

```text
Idempotency-Key / duplicate request replay
→ T17

Cross-module contracts
→ T18

Events / Outbox
→ T19

Worker claiming
→ T21

Exact GroupSeatInventory table/schema beyond the required T15/T16 fields
Exact occupancy query/projection implementation
→ Enrollments implementation

Payment/Ledger concurrency details beyond RecordCashPayment
→ financial decisions

Global API ETag policy
→ API standards if needed
```

---

# القرار النهائي المختصر

> EduCenterOS تستخدم **Optimistic Concurrency** كخيار افتراضي بواسطة `int Version` مُدارة من التطبيق، مع Database Constraints كآخر خط دفاع. عند conflict لا نعيد `SaveChanges` بشكل أعمى؛ نعيد قراءة authoritative state ونصنف السيناريو إلى stale conflict أو semantic success أو safe recompute أو security failure.

> الـPessimistic locking باستخدام `SELECT ... FOR UPDATE` تُستخدم فقط للـUse Cases القصيرة التي لديها controlling row واضحة وتحتاج serialization فعلية.

> بالنسبة للمقاعد، بعد نقل Enrollment Capacity إلى `Enrollments`، كل عملية تغير `EnrollmentCapacity` أو `OccupiedCapacity` يجب أن تقفل **نفس Seat controlling row** داخل Local Transaction. بذلك طلبان على آخر مقعد لا يمكن أن ينجحا معًا، ولا نحتاج Cross-Module Lock أو Serializable كحل افتراضي.

> Enrollments تحتفظ كذلك بـ`MaxAllowedEnrollmentCapacitySnapshot` ونسخة Academic مقبولة. خفض الحد يمر على Enrollments أولًا، ورفعه لا يصبح متاحًا قبل تفعيله في Academic، وكل تغيير يتنافس على نفس Seat row.

> IdentityAccess تستهلك Credentials/Proofs أحادية الاستخدام ذريًا بترتيب Locks ثابت، وتصدر Ticket واحدة لكل StepUpGrant. `RecordCashPayment` تستخدم الاستثناء الذري المعتمد مع ترتيب `CashDrawer → CashShift` حتى تكون Payment وCashMovement كلاهما أو لا شيء.

> الـRetries ليست عامة: فقط الأخطاء والـflows المسموح بها تعيد whole transaction بContext جديدة وميزانية محدودة، بينما Commit ambiguity تنتقل إلى T17. كل concurrency-sensitive flow يجب إثباته باختبارات متزامنة deterministic على PostgreSQL الحقيقية، مع فحص final database state وليس مجرد HTTP response.
