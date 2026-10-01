# T15 - Transactions

## القرار

EduCenterOS يعتمد:

```text
Local Transaction per Business Module
+
Top-level Command Handler owns transaction boundary
+
EF Core implicit transaction for single SaveChanges
+
Explicit transaction only when needed
+
Read Committed by default
+
Local transaction is the default across Module boundaries
+
One approved same-database atomic exception for RecordCashPayment
+
No TransactionScope / Distributed Transactions
+
No external network calls inside open DB transaction
+
Transactional Outbox for every required durable follow-up
+
Commit before client-visible success
```

---

# 1. Transaction Boundary

الـTransaction تتحدد من الـBusiness invariant.

السؤال:

```text
What state must change atomically
to prevent an invalid business state?
```

وليس:

```text
How many repositories?
How many code lines?
How many tables?
```

---

# 2. مالك الـTransaction

المالك الافتراضي:

```text
Top-level Command Handler
```

في Local Use Case تكون Handler الخاصة بالـModule هي المالكة المنطقية للـboundary، ويمكن Infrastructure helper صغيرة تنفيذ begin/commit/rollback بدون إخفاء نطاق العملية.

الاستثناء المعتمد `RecordCashPayment` تملكه Feature-specific application orchestrator موثقة في القسم 8؛ لا يملكها Endpoint ولا Module عشوائية.

الـEndpoint لا تبدأ Transaction.

الـDomain لا تعرف:

```text
DbContext
DbTransaction
SaveChanges
TransactionScope
```

---

# 3. DbContext = Local Unit of Work

كل Business Module تستخدم DbContext الخاصة بها.

```text
One Module
→ One owning DbContext
→ One local transaction
```

ولا ننشئ:

```text
Generic IUnitOfWork
Global TransactionManager
CrossModuleTransactionCoordinator
```

بدون احتياج حقيقي.

---

# 4. Single SaveChanges

لو كل تغييرات الـUse Case يمكن تجهيزها ثم حفظها بـ:

```csharp
await dbContext.SaveChangesAsync(cancellationToken);
```

فده يكفي.

EF Core تستخدم Transaction ذرية لـ`SaveChanges` الواحدة على PostgreSQL.

لا نضيف:

```text
BeginTransaction
→ SaveChanges
→ Commit
```

لمجرد الشكل.

نترك:

```text
AutoTransactionBehavior = WhenNeeded
```

ولا نضبط `Never` في Core V1.

`ExecuteUpdate` و`ExecuteDelete` وraw SQL تنفذ كل call منها كعملية مستقلة ما لم توجد Explicit Transaction. لذلك لو جمع Use Case أكثر من call أو خلطها مع `SaveChanges` وتحتاج Atomicity، يجب أن تكون داخل Explicit Transaction واحدة.

---

# 5. Explicit Transaction

نستخدمها فقط لما العملية تحتاج أكثر من Persistence step ذرية.

مثل:

```text
Lock authoritative row
+
re-check invariant
+
write state
```

أو:

```text
SaveChanges
+
module-owned SQL operation
+
another required persistence step
```

افتراضيًا كلها داخل نفس Module؛ الاستثناء الوحيد موثق صراحة في القسم 8.1.

---

# 6. Isolation Level

الـDefault:

```text
Read Committed
```

ولا نستخدم:

```text
Serializable everywhere
```

`Repeatable Read` أو `Serializable` تستخدم فقط عندما invariant محددة تحتاجها ومع Integration Tests تثبت ذلك.

اختيار Isolation أعلى لا يعوض:

```text
Missing constraint
Missing concurrency design
Wrong ownership
```

---

# 7. Start Late, Commit Early

الـExplicit Transaction تبدأ في آخر لحظة ممكنة وتنتهي في أول لحظة صحيحة.

قبلها يمكن تنفيذ:

```text
Request validation
Normalization
Authorization resolution
Safe cross-module reads
Pure calculations
```

وبعد فتحها نحتفظ فقط بالعمل الذي يحتاج Atomicity.

---

# 8. Cross-Module Transactions

القاعدة الافتراضية:

```text
Module A DbContext
+
Module B DbContext
+
One shared transaction
→ Not allowed as normal architecture
```

حتى لو الاثنين على نفس PostgreSQL Database.

وجود Database واحدة:

```text
≠ shared ownership
≠ shared transaction boundary
```

لكن المنع ليس عقيدة مطلقة. يسمح Same-database atomic exception فقط لو اجتمعت الشروط التالية:

```text
Immediate consistency is a real business requirement
Ownership cannot be moved without distorting the model
Both Modules are in the same process and PostgreSQL database
No network/external call occurs inside the transaction
Each Module writes only through its own module-owned participant/contract
One shared DbConnection + DbTransaction is used explicitly
No TransactionScope and no distributed transaction
Stable lock ordering and PostgreSQL integration tests exist
The exception is named and documented; no generic coordinator is exposed
```

## 8.1 Approved Core V1 Exception: RecordCashPayment

الاستثناء الوحيد المعتمد حاليًا:

```text
StudentFinance.Payment
+
BranchFinance.CashMovement
```

عند تسجيل Cash Payment بواسطة موظف تحصيل، البزنس تشترط Shift/Drawer صالحة وأن تظهر الحركة النقدية مع Payment بدون partial database state.

الـflow:

```text
Validate actor / permission / Step-up ticket when required
↓
Validate caller-supplied OperationId / Idempotency key / scope / fingerprint
↓
Begin one explicit PostgreSQL DbTransaction on shared connection
↓
StudentFinance participant claims the T17 idempotency record
using ON CONFLICT DO NOTHING RETURNING
↓
If claim lost → resolve committed replay/mismatch without business mutation
If claim won → continue
↓
BranchFinance participant locks and re-checks CashShift/Drawer eligibility
↓
StudentFinance participant creates Payment using pre-generated UUID v7 PaymentId
↓
BranchFinance participant creates CashMovement referencing PaymentId
↓
Each participant saves through its owning DbContext enlisted in the same DbTransaction
↓
Persist required Outbox records
↓
Complete StudentFinance idempotency result
↓
Commit once
↓
Return success
```

القيود:

- `RecordCashPaymentOrchestrator` لا تحقن Foreign DbContext ولا تعدل foreign tables مباشرة.
- `RecordCashPaymentOrchestrator` مملوكة لـStudentFinance application layer؛ StudentFinance تستخدم participant داخلية، بينما BranchFinance تعرض public participant contract ضيقة مع internal implementation طبقًا لـT18، ويحافظ ذلك على اتجاه `StudentFinance → BranchFinance.Contracts` بدون reference عكسية.
- StudentFinance تملك Idempotency record، وتحفظها داخل نفس shared transaction دون منحها ملكية CashMovement.
- `OperationId` و`Idempotency-Key` تتبعان T17، وتظلان ثابتتين في كل retry لنفس intent.
- BranchFinance تفرض uniqueness على source `(StudentPayment, PaymentId)`.
- كل Context تشترك في نفس open connection/transaction صراحة باستخدام provider-supported enlistment.
- أي فشل قبل Commit يعمل rollback للـPayment والـCashMovement معًا.
- Commit ambiguity تحل بواسطة OperationId/T17، وليس بإنشاء Payment جديدة.
- لا نستخدم هذه السابقة لتجميع أي عمليتين عشوائيتين داخل Transaction واحدة.

أي استثناء إضافي يحتاج Decision/ADR مستقلة تعدل قائمة الاستثناءات في T15.

---

# 9. لو Invariant تعبر أكثر من Module

الترتيب:

```text
1. Review ownership
2. Ask whether consistency must be immediate
3. Move controlling state to one owner when appropriate
4. Use local transaction there
5. Use Events / Outbox for follow-up when eventual consistency is acceptable
6. If immediate consistency remains essential, request a named same-database exception
```

ولا نحل المشكلة بـCross-Module Lock.

## 9.1 Core V1 Consistency Matrix

```text
Seat reservation / confirmation
→ Local atomic transaction in Enrollments

Payment + allocations
→ Local atomic transaction in StudentFinance

Cash Payment + CashMovement
→ Approved same-database atomic exception

Enrollment confirmed → Charge generation
→ Outbox + idempotent eventual consumer

Attendance finalized → Teacher compensation input
→ Outbox + idempotent eventual consumer

Notifications / Reporting projections
→ Outbox + eventual processing

StepUpGrant → sensitive operation
→ OperationAuthorizationTicket + target-module idempotency
```

---

# 10. Seat Capacity Ownership

دي أهم إضافة في T15.

الـhard local invariant:

```text
Occupied Seats
<=
Enrollment Capacity
<=
Max Allowed Enrollment Capacity Snapshot
```

حيث:

```text
Occupied Seats =
Confirmed Enrollments
+
Active Seat Reservations
+
Other policy-counted states
```

كل الحالات التي تستهلك المقاعد مملوكة لـ:

```text
Enrollments
```

لذلك القيم الثلاثة التي تحسم قبول الحجز تكون داخل `Enrollments` وقت القرار، والـcontrolling capacity تظل مملوكة لنفس Module.

---

# 11. Academic vs Enrollment Capacity

`Academic` تظل تملك:

```text
StudyGroup definition
Subject / Grade
Primary Teacher
Schedule
Room assignment
Group lifecycle
Room physical capacity when applicable
```

أما `Enrollments` فتملك:

```text
Enrollment Capacity
Seat availability
Seat reservations
Confirmed enrollments
Waitlist
Enrollment admission rules
```

وبالتالي نفرق بين:

```text
Room Physical Capacity
→ Academic

Enrollment Capacity
→ Enrollments
```

والـEnrollment Capacity لا يجوز أن تتجاوز القيود الفيزيائية أو التشغيلية التي تفرضها الـAcademic facts.

---

# 12. Group Enrollment Policy

بدل وضع `Capacity` كـcontrolling state داخل `StudyGroup`، يمكن أن يكون داخل Enrollments مفهوم مثل:

```text
GroupEnrollmentPolicy
or
GroupSeatInventory
```

مثلًا مفهوميًا:

```text
GroupId
EnrollmentCapacity
MaxAllowedEnrollmentCapacitySnapshot
AcademicConstraintVersion
ConstraintStatus
Version
```

ولا نثبت الاسم أو الـschema النهائية في T15.

المهم:

> الـRow التي تتحكم في السماح بحجز المقعد موجودة داخل `Enrollments`.

`MaxAllowedEnrollmentCapacitySnapshot` ليست Cache اختيارية للعرض؛ هي النسخة المحلية المقبولة من القيد الأكاديمي/الفيزيائي التي تعتمد عليها كتابة المقاعد. وتحدث فقط من خلال Workflow مُرقّمة وIdempotent مع `Academic`.

---

# 13. Reserve Seat Flow

التصميم يصبح:

```text
Validate actor / institution
↓
Read Academic facts for eligibility/display when needed
↓
Begin Enrollments local transaction
↓
Load/lock authoritative seat state
↓
Re-check occupied <= enrollment capacity <= accepted local academic limit
↓
Create SeatReservation
↓
Save
↓
Commit
```

وبالتالي آخر مقعد لا يحتاج:

```text
Academic lock
+
Enrollments lock
+
cross-module transaction
```

أي Academic read قبل الـTransaction قد تصبح stale، ولذلك لا تستخدم كالدليل الوحيد على القيد الصلب وقت الكتابة. القرار النهائي يعتمد على الـaccepted local constraint snapshot داخل نفس Enrollments transaction.

---

# 14. Changing Enrollment Capacity

تغيير Enrollment Capacity نفسه يتم داخل:

```text
Enrollments
```

ويتحقق من:

```text
NewCapacity
>=
Current OccupiedCapacity

NewCapacity
<=
MaxAllowedEnrollmentCapacitySnapshot
```

في نفس consistency boundary.

## 14.1 Changing the Academic/Physical Limit

تغيير القيد الأكاديمي/الفيزيائي نفسه لا يعتمد على قراءة عابرة، وترتيب الخطوات يعتمد على اتجاه التغيير.

### خفض الحد أو الانتقال لقيد أشد

الـWorkflow المعتمدة:

```text
Academic proposes constraint change
(ConstraintChangeId + GroupId + proposed limit + next version)
↓
Enrollments receives idempotent request
↓
Begin local Enrollments transaction
↓
Lock GroupSeatInventory
↓
If OccupiedCapacity > proposed limit → reject
Otherwise store new MaxAllowedEnrollmentCapacitySnapshot/version
and lower EnrollmentCapacity when needed
↓
Commit and return accepted version
↓
Academic activates the room/group change only after acceptance
```

القواعد:

- `ConstraintChangeId` و`AcademicConstraintVersion` يمنعان التكرار والترتيب العكسي.
- لو Enrollments رفضت الخفض، Academic لا تفعل التغيير الذي يكسر السعة.
- لو Enrollments قبلت ثم فشل تفعيل Academic، يظل Enrollments على قيد أشد، وهي حالة آمنة؛ ويمكن إرسال compensating change جديدة بصورة Idempotent.
- لا تفتح المجموعة لحجز المقاعد أول مرة قبل وجود Active accepted constraint snapshot داخل Enrollments.
- شكل Contract/Event النهائي في T18/T19، والـlocking/version behavior الدقيق في T16.

### رفع الحد أو الانتقال لقيد أوسع

الترتيب ينعكس للحفاظ على الأمان:

```text
Academic activates the higher physical/academic limit first
↓
publishes/sends versioned activation
↓
Enrollments idempotently verifies monotonic version
↓
stores the higher MaxAllowedEnrollmentCapacitySnapshot
```

لو تحديث Enrollments فشل، تظل على الحد الأقدم الأشد، وهي حالة آمنة. قبول الحد الأعلى لا يرفع `EnrollmentCapacity` تلقائيًا؛ هو يسمح بأمر مستقل داخل Enrollments أن يرفعها لاحقًا بعد فحص الـlocal invariant.

بهذا تظل الـseat invariant محلية، وفي نفس الوقت لا يسمح سباق بين تغيير القاعة وحجز مقعد بتجاوز القيد المقبول.

---

# 15. External Side Effects

ممنوع داخل open transaction انتظار:

```text
SMS
Email
Payment Provider
File Storage
Webhook
External HTTP API
Message Broker
```

القاعدة:

```text
Business state
+
Required Outbox record
↓ same owning transaction
Commit
↓
Dispatcher performs external delivery
```

لو الـfollow-up مطلوبة لصحة الـworkflow فلا يكفي:

```text
Task.Run
in-memory queue
direct after-commit call only
```

لأن crash بعد Commit قد يفقدها. الـdirect after-commit execution يسمح به فقط لـbest-effort telemetry غير المطلوبة ويمكن فقدها صراحة.

الـOutbox قاعدة معمارية إلزامية من T15؛ T19 تحدد schema والdispatcher والdelivery semantics. أي Feature تحتاج durable cross-module/external follow-up لا تعتبر مكتملة قبل تنفيذ هذا الجزء من T19.

---

# 16. Database Rollback ≠ Business Compensation

لو Provider خارجي نفذ Side Effect:

```text
DB rollback
```

لا تلغيه.

لذلك:

```text
Database rollback
≠
Refund
≠
Provider cancellation
≠
Compensation
```

الـlong-running workflows لها state machine / compensation خاصة بها.

---

# 17. Cross-Module Calls أثناء Transaction

الـDefault:

> لا نستدعي Module أخرى بعد فتح Local Write Transaction.

لو الـUse Case تحتاج Fact من Module أخرى:

```text
Contract read/check
↓
Begin owning Module transaction
↓
local authoritative re-check
↓
write
↓
commit
```

لو الـFact الخارجية نفسها لازم تتقفل atomically مع الكتابة:

> نراجع ownership أولًا، ثم نطلب named exception فقط لو استوفت شروط القسم 8.

الاستثناء المعتمد `RecordCashPayment` يسمح فقط باستدعاء الـin-process participants المحددة من StudentFinance وBranchFinance بعد فتح الـshared transaction. لا يسمح بأي Network call أو arbitrary module query، وكل participant تكون DbContext الخاصة بها enlisted صراحة في نفس `DbTransaction`.

أي Cross-module read قبل Local Transaction هي Snapshot قابلة للتقادم، وليست Lock ولا ضمانًا ذريًا.

---

# 18. Authorization & Transactions

Authorization قبل الـTransaction لا تعني إن أي mutable security/business state أصبحت محمية من Race.

في العمليات الحساسة قد نحتاج:

```text
Authoritative re-read
+
Concurrency protection
```

حسب T16.

لكن لا نفتح Transaction بين Business Module و`InstitutionsDbContext` لمجرد تثبيت Authorization.

## 18.1 Authorization Linearization

الـstandard authorization تحسم قبل بدء Business transaction. أي revoke/change يحدث قبل التحقق يمنع العملية؛ أما التغيير الذي يحدث بعد بدء الـlocal atomic mutation فلا يلغيها بأثر رجعي، إلا لو الـUse Case صممت صراحة Lease/Ticket قابلة لإعادة الفحص داخل نفس ownership boundary.

العمليات المؤجلة أو الطويلة تعيد Authorization عند التنفيذ وفق T14، ولا تعتمد على قرار قديم من لحظة enqueue.

## 18.2 High-risk Step-up Handoff

لا نستهلك `StepUpGrant` داخل Transaction مشتركة بين IdentityAccess والـBusiness Module.

الـflow المعتمدة:

```text
Client supplies StepUpGrant for exact purpose/target
↓
IdentityAccess atomically validates and consumes the grant
↓
IdentityAccess issues short-lived OperationAuthorizationTicket
bound to session + target module + operation + resource + OperationId
↓
Target Module validates the ticket before its local transaction
↓
Target transaction stores TicketId + OperationId with uniqueness
and performs the sensitive mutation
↓
Commit
```

الـTicket تحمل/ترتبط على الأقل بـ:

```text
TicketId
StepUpGrantId
UserAccountId
UserSessionId
TargetModule
OperationCode / Purpose
TargetResourceId?
OperationId
AchievedAssuranceLevel
IssuedAtUtc
ExpiresAtUtc
```

القواعد:

- الـbaseline لعمر Ticket دقيقتان ولا تتجاوز Session أو Grant الأصلية.
- Raw ticket عالية العشوائية، لا تحفظ أو تسجل كنص، ولا تمر في URL.
- لا تقبل مع Session أو Module أو Operation أو Resource أو `OperationId` مختلفة.
- الـBusiness Module تسجل `TicketId` و`OperationId` في نفس local transaction الخاصة بالعملية؛ unique constraints تمنع تنفيذها لنية مختلفة أو مرتين.
- لو فشلت target transaction يمكن إعادة نفس العملية بنفس `OperationId` والTicket قبل انتهائها؛ الـStepUpGrant الأصلية تظل مستهلكة.
- لو ضاعت الاستجابة بعد Commit، يحسم T17 النتيجة بنفس `OperationId` بدل إعادة mutation.
- لو انتهت Ticket قبل نجاح العملية، ينفذ المستخدم Step-up جديدة. لا نعيد إحياء Grant مستهلكة.
- Replay لعملية Completed في T17 تعيد Current Authorization لكنها لا تشترط Ticket الأصلية ما زالت حية؛ Fresh Ticket مطلوبة فقط عندما لا توجد Completed operation.

التخزين والإصدار تتبع T13، وسياسة الطلب تتبع T14، والـidempotent result replay تتبع T17.

---

# 19. Concurrency

Transaction لا تحل Race Conditions وحدها.

T16 تحدد:

```text
Version tokens
FOR UPDATE
Unique constraints
Serializable when justified
Retry semantics
```

والـTransaction توفر الـboundary التي تعمل داخلها الآلية المناسبة.

---

# 20. Idempotency

Transaction لا تمنع Duplicate Intent.

مثل:

```text
POST booking
→ committed
→ response lost
→ client retries
```

دي مسؤولية T17.

إذًا:

```text
Transaction
→ atomic execution

Concurrency
→ competing intents

Idempotency
→ same intent delivered repeatedly
```

ثلاث مشاكل مختلفة.

---

# 21. Commit Ambiguity

لو حصل Exception أثناء/بعد إرسال `COMMIT` ومش معروف هل PostgreSQL التزمت أم لا:

```text
Do not assume rollback
Do not blindly retry
```

نستخدم Idempotency / reconciliation حسب T17 والـDomain.

## 21.1 Cancellation أثناء Commit

- قبل بدء mutation يمكن احترام Request cancellation بصورة عادية.
- بعد إرسال Commit، cancellation أو انقطاع الاتصال لا يثبت أن PostgreSQL عملت rollback.
- لا نرجع نتيجة مؤكدة `Cancelled` أو `Failed` لو outcome مجهولة.
- نستخدم `OperationId`/status lookup/reconciliation في T17 لحسم النتيجة.

---

# 22. Success after Commit

ممنوع إرسال:

```text
Success
Credential
Durable business event
```

للClient أو نظام خارجي قبل نجاح الـCommit المطلوبة.

---

# 23. Outbox

لكل durable follow-up مطلوبة:

```text
Business changes
+
Outbox records
```

تتحفظ في نفس Local Transaction.

لكن:

```text
Actual delivery
```

تحدث بعد الـCommit.

لا نحتاج Message Broker لمجرد استخدام Transactional Outbox.

كل Module تكتب Outbox record داخل schema/DbContext التي تملك الـbusiness change. وفي الاستثناء `RecordCashPayment` يمكن لكل participant كتابة records التي تملكها داخل نفس shared PostgreSQL transaction.

---

# 24. Bulk Operations

كل Bulk Use Case تحدد صراحة:

```text
Atomic
or
Partial / per batch
```

لو Atomic:

```text
all succeed
or
none succeed
```

ولو الحجم يجعل الـTransaction طويلة جدًا، نعيد تصميم الـworkflow بدل فتح Transaction ضخمة.

---

# 25. Retries

ممنوع:

```text
catch DbException
→ retry automatically
```

الـRetry تحتاج:

```text
Failure classification
+
Known rollback state
+
Replay-safe operation
+
Bounded policy
```

وتفاصيل Concurrency retry تتبع T16.

في Core V1 لا نفعل blanket `EnableRetryOnFailure` للـwrite flows. T16 تعتمد retry matrix صريحة وwhole-transaction retry بميزانية محدودة، بينما T17 تحسم duplicate intent وcommit reconciliation.

لو اعتمد Retry لعملية تستخدم Explicit Transaction:

- يعاد تشغيل transaction delegate كاملة عبر EF execution strategy، وليس `SaveChanges` أو `Commit` منفردة.
- العملية يجب أن تكون Replay-safe ومقيدة بـ`OperationId`/Idempotency مناسبة.
- failure class وعدد المحاولات/backoff يحددان صراحة في T16.
- commit ambiguity لا تدخل blind automatic retry.
- لا يعاد استخدام in-memory Domain state قد تكون تغيرت من محاولة سابقة بدون إعادة تحميل/بناء صحيحة.

---

# 26. Testing

أي Transaction-sensitive Use Case تختبر على PostgreSQL حقيقية.

لا نعتمد على:

```text
EF InMemory
SQLite
```

لإثبات PostgreSQL transaction semantics.

الاختبارات المهمة تثبت:

```text
Success
Rollback
Constraint failure
Concurrency
Tenant write guards
No partial state
```

وتغطي خصوصًا:

```text
Failure between participant SaveChanges and shared commit
Cash payment racing with cash-shift close
Payment/CashMovement both commit or both rollback
Business change and required Outbox record commit atomically
Crash after commit and before dispatch
Concurrent reuse of one OperationAuthorizationTicket
Same ticket with wrong module/operation/resource/OperationId
Target failure then safe same-operation retry before ticket expiry
Commit ambiguity resolved by OperationId
No duplicate effect after an approved retry
Academic limit reduction racing with seat reservation
Academic limit increase unavailable to enrollment before activation
```

---

# القواعد النهائية

```text
Local transaction per Module
Top-level Handler owns boundary
Single SaveChanges → implicit transaction
Explicit transaction only when needed
Read Committed default

No request-wide transaction
No unapproved cross-module shared transaction
RecordCashPayment is the only approved same-database exception
No TransactionScope
No distributed transaction

No network call inside open DB transaction
No success before commit
Required durable follow-up → same-transaction Outbox
No blanket transient retry

Concurrency → T16
Idempotency → T17
Module communication → T18
Outbox → T19
```

وأهم Domain ownership rule:

```text
Enrollment Capacity
+
Seat Reservations
+
Confirmed Enrollments
→ owned by Enrollments
```

حتى تكون Seat Availability invariant محمية داخل Local Transaction واحدة.

---

# خارج نطاق T15

T15 لا تحدد:

```text
Exact concurrency mechanism
Version token details
FOR UPDATE policy
Retryable failure classes / count / backoff / lock ordering
→ T16

Idempotency storage / fingerprint / result replay / commit reconciliation
→ T17

Cross-module participant contracts
Academic constraint-change contract
→ T18

Domain/Integration Events
Outbox schema/dispatcher
→ T19

Exact GroupSeatInventory schema/name
→ Enrollments implementation decision

Approval workflow tickets beyond authentication Step-up
→ T27
```

---

# القرار النهائي المختصر

> EduCenterOS تعتمد Transactions محلية قصيرة داخل الـBusiness Module المالكة للـinvariant. الـTop-level Command Handler تملك الـtransaction boundary، و`SaveChangesAsync` واحدة تعتمد EF Core implicit transaction، بينما Explicit Transaction تستخدم فقط عند وجود سبب فعلي.

> لا نستخدم Shared/Cross-Module Transactions كحل عام، ولا `TransactionScope` أو Distributed Transactions. الاستثناء الوحيد المعتمد في Core V1 هو `RecordCashPayment` بين StudentFinance وBranchFinance على نفس PostgreSQL transaction، عبر participants مملوكة لموديولاتها واختبارات تكامل صريحة.

> لذلك **Enrollment Capacity التشغيلية تنتقل إلى `Enrollments`** مع Seat Reservations وConfirmed Enrollments، وتحتفظ بنسخة مقبولة ومُرقّمة من الحد الأكاديمي/الفيزيائي. `Academic` لا تفعل خفضًا لهذا الحد إلا بعد قبول Enrollments له، وبذلك لا يسمح السباق بين تغيير القاعة وحجز مقعد بتجاوز السعة.

> الـTransactions لا تستبدل Concurrency أو Idempotency أو Outbox، ولا تحتوي Network Side Effects. الـStep-up تنتقل للـBusiness Module بتذكرة عملية قصيرة ومقيدة، والـfollow-up المطلوبة تحفظ في Outbox داخل نفس transaction. Success لا تظهر قبل نجاح الـCommit، وكل flow حساسة تُثبت باختبارات PostgreSQL حقيقية.
