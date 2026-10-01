# T06 - Domain Design Rules

## الهدف من القرار

تحديد الطريقة الموحدة لتصميم الـDomain داخل EduCenterOS، بحيث:

- Business invariants المهمة تتحمى داخل الكود الصحيح.
- الـHandlers تنظم Use Cases ولا تصبح God Services.
- نتجنب Anemic Domain Model في الأجزاء المعقدة.
- نتجنب تطبيق DDD بشكل مبالغ فيه على CRUD بسيط.
- Entities/Aggregates تبدأ وتظل في حالات صحيحة.
- Ownership والـtransactional invariants تكون واضحة.
- Cross-module rules لا تتحول إلى hidden coupling.
- Concurrency وTransactions لا تختلط مع Domain modeling.

---

# القرار النهائي

EduCenterOS يعتمد:

```text
Pragmatic Rich Domain Model
```

يعني:

```text
Rich Domain Model
→ للأجزاء ذات Business Rules الحقيقية

Simple CRUD / Application Logic
→ للأجزاء البسيطة
```

ونستخدم:

```text
Entities
Aggregates
Value Objects
Domain Services
Domain Events
```

فقط عندما تضيف معنى فعليًا.

ولا نفرض:

```text
Repository لكل Entity
Factory لكل Entity
Specification لكل Query
Domain Event لكل Save
DDD لكل Lookup table
```

---

# 1. Handler vs Domain

## Handler

الـHandler مسؤولة عن orchestration.

مثل:

```text
Read current actor/context
Authorization

Load required state

Read facts from other modules when required

Application-level validation

Call Domain methods

Manage transaction when required

Save changes

Return Result / Result<T>
```

الـHandler يمكن أن تنظم أكثر من خطوة.

لكن لا يجب أن تتحول إلى المكان الوحيد الذي يحتوي كل قواعد الـBusiness.

---

## Domain

الـDomain تحمي القواعد التي تخص state التي تملكها.

مثل:

```text
Invalid state transition
Invalid financial reversal

Cannot finalize attendance twice

Teacher replacement effective date invalid

Cannot reduce local enrollment capacity below occupied seats
```

لكن Domain Entity لا تسأل Module أخرى ولا تقرأ Database بنفسها.

---

# 2. أنواع الـInvariants

نفرق بين 3 أنواع.

## A. Aggregate-local invariant

كل البيانات المطلوبة موجودة داخل نفس Aggregate.

مثال:

```text
Session end > session start
```

مكانها الطبيعي:

```text
Domain Entity / Aggregate
```

---

## B. Same-module cross-aggregate invariant

البيانات المطلوبة داخل نفس Business Module لكنها موزعة على أكثر من Aggregate/row.

مثال:

```text
OccupiedCapacity
=
Reservations
+
Enrollments
```

والـcontrolling state داخل نفس `Enrollments` Module.

الـApplication layer تنظم:

```text
Load / lock state
↓
Evaluate Domain rule
↓
Mutate
↓
Save in local transaction
```

---

## C. Cross-module rule

القرار يعتمد على Fact من Module أخرى.

مثال:

```text
EnrollmentCapacity
<=
Academic allowed physical limit
```

المسار:

```text
Read authoritative Academic Fact
↓
Pass value to owning Use Case
↓
Apply Enrollments-owned rule
↓
Persist Enrollments state
```

لكن ممنوع:

```text
Domain Entity
→ call another Module
```

---

# 3. Strong Invariant Ownership

لو Business correctness تحتاج:

```text
State A
+
State B
```

أن تتغير أو تتقفل Atomically،

السؤال الأول:

> هل الـownership صحيحة؟

مش:

> نعمل Cross-Module Transaction إزاي؟

ده السبب إن:

```text
EnrollmentCapacity
SeatReservation
Enrollment
```

كلهم في `Enrollments`.

بينما:

```text
StudyGroup academic definition
Room
Schedule
Teacher
```

تظل في `Academic`.

---

# 4. تصميم الـEntities

الـEntity كائن له Identity تستمر حتى مع تغير properties.

أمثلة:

```text
StudyGroup
StudentProfile
Institution
Enrollment
Payment
TeacherContract
```

الـdefault:

```csharp
internal sealed class StudyGroup
{
    public Guid Id { get; private set; }

    public GroupStatus Status { get; private set; }

    public Guid PrimaryTeacherId { get; private set; }

    private StudyGroup()
    {
        // EF Core
    }
}
```

لاحظ:

> `StudyGroup` لم تعد تحمل EnrollmentCapacity باعتبارها Seat controlling state.

---

# 5. Entity Rules

1. Setters تكون `private` افتراضيًا.
2. Mutations المهمة تتم من خلال Domain Methods.
3. Handler لا تعدل state الحساسة مباشرة.
4. Parameterless constructor تكون `private` لو مطلوبة لـEF Core.
5. Entity تبدأ في valid state.
6. ممنوع:

```text
Create invalid entity
→ fix it later externally
```

---

# 6. إنشاء الـEntities

## Constructor مباشر

مناسب للكيانات البسيطة.

مثال:

```csharp
internal sealed class ExpenseCategory
{
    internal ExpenseCategory(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    public Guid Id { get; }

    public string Name { get; private set; }
}
```

---

## Factory Method

تستخدم لما الإنشاء يحتوي:

```text
Multiple business rules
Initial lifecycle state
Child entities

Expected failure
Code generation
Value object creation
Domain event
```

مثال:

```csharp
internal static Result<StudyGroup> Create(
    Guid id,
    Guid institutionId,
    Guid branchId,
    Guid primaryTeacherId)
{
    if (primaryTeacherId == Guid.Empty)
    {
        return Result.Failure<StudyGroup>(
            GroupErrors.PrimaryTeacherRequired);
    }

    var group = new StudyGroup
    {
        Id = id,
        InstitutionId = institutionId,
        BranchId = branchId,
        PrimaryTeacherId = primaryTeacherId,
        Status = GroupStatus.Draft
    };

    return Result.Success(group);
}
```

قاعدة:

> لا Factory لكل Entity؛ فقط عندما الإنشاء فعلًا يحتاج حماية.

---

# 7. تغيير الحالة باستخدام Domain Methods

ممنوع:

```csharp
group.PrimaryTeacherId = request.TeacherId;
```

نفضل:

```csharp
var result = group.ChangePrimaryTeacher(
    teacherId,
    effectiveFrom,
    reason);
```

بحيث الـDomain تحمي lifecycle rules التي تملكها.

---

# 8. Seat Capacity ليست StudyGroup Rule

الـpattern القديم:

```csharp
group.ChangeCapacity(
    newCapacity,
    occupiedCapacity);
```

لم يعد معتمدًا للـEnrollment Capacity.

لأن:

```text
StudyGroup
→ Academic

SeatReservation
Enrollment
EnrollmentCapacity
→ Enrollments
```

فلو `StudyGroup` استقبلت occupied count من Module أخرى عشان تقرر Capacity، نكون عملنا Strong Invariant موزعة على Module boundaries.

---

# 9. Seat Capacity Domain Model

المفهوم يمكن أن يكون داخل Enrollments مثل:

```text
GroupSeatInventory
or
GroupEnrollmentPolicy
```

مثال مفاهيمي:

```csharp
internal Result ChangeEnrollmentCapacity(
    int newCapacity,
    int occupiedCapacity)
{
    if (newCapacity <= 0)
    {
        return Result.Failure(
            EnrollmentErrors.InvalidCapacity);
    }

    if (newCapacity < occupiedCapacity)
    {
        return Result.Failure(
            EnrollmentErrors.CapacityBelowOccupiedSeats);
    }

    EnrollmentCapacity = newCapacity;

    Version++;

    return Result.Success();
}
```

لكن:

> الـDomain method وحدها لا تحل Concurrent Race.

T16 هي المسؤولة عن:

```text
FOR UPDATE
Controlling-row lock
Version
Concurrency tests
```

---

# 10. Seat Mutation Boundary

أي عملية تؤثر على:

```text
OccupiedCapacity
or
EnrollmentCapacity
```

يجب أن تمر من نفس consistency boundary داخل Enrollments.

مثل:

```text
Create SeatReservation
Confirm direct Enrollment
Release Reservation

Cancel counted Enrollment
Expire counted Reservation

Transfer into group

Change Enrollment Capacity

Waitlist Offer → Reservation
```

---

# 11. Academic Facts

Academic تملك Facts مثل:

```text
Group exists
Group academic status

Room
Room physical capacity

Schedule
Teacher assignment
```

Enrollments يمكن أن تستخدم Facts دي.

لكن:

```text
Academic Fact
≠ Seat controlling state
```

ولا نستخدم:

```text
Academic event
→ decrement capacity later
```

لحماية overbooking.

Strong Seat invariant لازم تظل local.

---

# 12. الـAggregates

Aggregate هي مجموعة Objects لازم تتغير معًا للحفاظ على invariant.

Aggregate Root هي المدخل لتعديل هذه المجموعة.

مثال مبدئي:

```text
StudyGroup
├── SchedulePatterns
└── TeacherAssignment history/configuration
```

حسب التصميم النهائي.

لو StudyGroup هي Aggregate Root:

```text
External code
❌ doesn't mutate child state directly
```

---

# 13. Aggregate Boundaries

تتحدد حسب:

```text
Transactional invariants
```

مش حسب:

```text
UI screen
Form
Database diagram
Foreign keys
```

السؤال:

> إيه البيانات اللي لازم تفضل صحيحة مع بعضها داخل نفس local transaction؟

---

# 14. Aggregates صغيرة

ممنوع Aggregate ضخمة مثل:

```text
Institution
├── All Branches
├── All Employees
├── All Students
├── All Groups
├── All Payments
└── All Expenses
```

لأنها تسبب:

```text
Huge object graphs
Contention
Heavy loading
Poor transaction boundaries
```

---

# 15. Aggregate References

الأصل:

```csharp
public Guid PrimaryTeacherId { get; private set; }
```

بدل Object Graph كاملة.

خصوصًا بين Modules مختلفة.

---

# 16. عملية واحدة تعدل Aggregate واحدة غالبًا

لو Use Case تحتاج أكثر من Aggregate:

نراجع:

```text
Is boundary wrong?
Is same local transaction required?
Can one aggregate own the invariant?
Can eventual consistency work?
```

Transactions تتبع T15.

Concurrency تتبع T16.

Events/Outbox تتبع T19.

---

# 17. أمثلة مبدئية للـAggregates

دي أمثلة وليست Final implementation.

## Academic

```text
StudyGroup
AcademicYear
Room
```

غالبًا مستقلة.

`StudyGroup` لا تحتوي:

```text
All students
All enrollments
All sessions
```

---

## Enrollments

ممكن تشمل Aggregates مثل:

```text
Booking
Enrollment
WaitlistEntry
GroupSeatInventory / GroupEnrollmentPolicy
```

كل Concept لها Lifecycle مختلفة.

---

## Institutions

```text
Institution
InstitutionMembership
MembershipInvitation
```

مش كل Memberships Child Collection داخل Institution.

---

## StudentFinance

```text
Payment
RefundRequest
PaymentReversal
```

ولا نحول Student إلى Aggregate تحمل كل Ledger history.

---

# 18. Value Objects

Value Object ليس له Identity مستقلة.

قيمته تحدده.

أمثلة:

```text
Money
Percentage
DateRange

PhoneNumber
EmailAddress

SystemCode
DisplayCode
```

القواعد:

```text
Immutable
Value equality
No independent ID
Protect its own rules
Use only when useful
```

ولا نحول كل:

```text
string
int
decimal
```

إلى Value Object لمجرد DDD.

---

# 19. Money

مهم:

> لا نضع قاعدة عامة أن `Money.Amount` يجب أن تكون non-negative دائمًا.

بعض الـDomain concepts قد تستخدم قيمة signed:

```text
Adjustment
Correction
Ledger delta
```

بينما Concepts أخرى يجب أن تكون موجبة:

```text
PaymentAmount
Price
ChargeAmount
```

لذلك:

```text
Sign rule
→ belongs to domain concept
```

وليس Money primitive نفسها بالضرورة.

نستخدم:

```text
decimal
+
explicit currency
```

طبقًا لـT07.

---

# 20. Domain Services

تستخدم لما rule:

```text
Business-important
No natural single Entity owner
Uses multiple domain concepts
Independent from HTTP / EF Core
```

مثال محتمل:

```text
TeacherCompensationCalculator
```

لو rule تخص Entity واحدة:

> مكانها الطبيعي داخل الـEntity.

أسماء مثل:

```text
GroupDomainService
StudentDomainService
PaymentDomainService
```

تحتاج مراجعة قوية لأنها غالبًا علامة على behavior اتسحب من الـEntities.

---

# 21. Domain Events

Domain Event تمثل Business fact حصلت.

أمثلة:

```text
GroupRegistrationOpened
PrimaryTeacherChanged

StudentEnrolled

PaymentReversed

InstitutionOwnershipTransferred
```

مش:

```text
EntitySaved
RowUpdated
PropertyChanged
```

---

# 22. Domain Event vs Integration Event

نفرق بينهم.

## Domain Event

داخل حدود Module.

تمثل Fact Domain داخلية.

```text
Domain Event
→ internal module concern
```

## Integration Event

Public cross-module contract.

```text
Integration Event
→ published for other modules
```

مش كل Domain Event لازم تصبح Integration Event.

وT19 تحسم:

```text
Dispatch
Outbox
Delivery
Consumer deduplication
```

---

# 23. Domain Events لا تحمي Strong Invariant

ممنوع Pattern مثل:

```text
Enrollment created
↓
Domain / Integration Event
↓
Academic later decreases remaining seats
```

إذا الهدف منع overbooking.

Events مناسبة للآثار التي تقبل eventual consistency:

```text
Notification
Reporting projection
Analytics update
non-critical follow-up
```

---

# 24. Expected Failure vs Exception

Expected business failures مثل:

```text
Group cancelled
No seat available

Duplicate code
Inactive contract

Invalid state transition
Refund not allowed
```

ترجع:

```text
Result
or
Result<T>
```

---

## Exceptions

للحالات غير المتوقعة:

```text
Bug
Corrupted state

Impossible internal condition

Unexpected infrastructure failure
```

قاعدة:

> Exceptions مش normal business flow.

---

# 25. Collections داخل Aggregate

المعتمد:

```csharp
private readonly List<GroupSchedulePattern> _schedulePatterns = [];

public IReadOnlyCollection<GroupSchedulePattern> SchedulePatterns
    => _schedulePatterns;
```

ممنوع:

```csharp
public List<GroupSchedulePattern> SchedulePatterns { get; set; }
```

التعديل:

```csharp
group.AddSchedulePattern(...);
group.RemoveSchedulePattern(...);
```

---

# 26. EF Core والـDomain

الاختيار الافتراضي:

```text
Fluent Configuration
inside Infrastructure
```

ممنوع Persistence Attributes داخل Domain افتراضيًا.

بدل:

```csharp
[Table("study_groups")]
[MaxLength(100)]
```

نستخدم:

```csharp
internal sealed class StudyGroupConfiguration
    : IEntityTypeConfiguration<StudyGroup>
{
    public void Configure(
        EntityTypeBuilder<StudyGroup> builder)
    {
        builder.ToTable("study_groups");

        builder.Property(x => x.DisplayCode)
            .HasMaxLength(100);
    }
}
```

الهدف:

```text
Domain
→ independent from persistence details as much as practical
```

---

# 27. Repositories

لا Repository لكل Entity.

## Queries

ممكن تستخدم EF Core Projection مباشرة:

```csharp
dbContext.StudyGroups
    .AsNoTracking()
    .Select(...)
```

---

## Simple Commands

ممكن Module DbContext مباشرة داخل Handler.

---

## Custom Repository

تستخدم فقط لو عندنا:

```text
Complex repeated aggregate loading
Special persistence logic

Business-meaningful persistence abstraction

Need to protect aggregate loading boundary
```

ممنوع:

```text
IGenericRepository<T>
```

---

# 28. Delete / Archive / Correction

لا يوجد:

```text
IsDeleted
```

عالمي لكل Tables.

---

## Historical / Financial records

مثل:

```text
Enrollment
Payment
Attendance

TeacherSettlement
AuditRecord
```

لا يتم حذفها لمجرد تصحيح خطأ.

نستخدم:

```text
Status
Cancellation
Reversal
Adjustment
Correction record
Archive
```

حسب الـDomain.

---

## Operational records

مثل:

```text
Room
ExpenseCategory
StudyGroup
Branch
```

تستخدم lifecycle مناسبة:

```text
Active
Inactive
Archived
Cancelled
```

---

## Hard Delete

فقط لما لا يوجد أثر Business/قانوني مهم.

مثل:

```text
Unused draft
Temporary file

Expired OTP after retention
Expired session after retention

Test data
```

---

# 29. Base Classes

مسموح Base Classes صغيرة.

مثال:

```csharp
internal abstract class Entity
{
    public Guid Id { get; protected init; }
}
```

وممكن:

```csharp
internal abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents
        => _domainEvents;
}
```

ممنوع Base Entity عامة تحمل:

```text
InstitutionId
BranchId

CreatedAt
CreatedBy

UpdatedAt
UpdatedBy

IsDeleted
DeletedAt

Version
Status
```

لأن مش كل Domain concept لها نفس المعنى.

---

# 30. Enums vs Lookup Data

نستخدم `enum` عندما القيم:

```text
Finite
Stable
Code-controlled
Part of behavior
```

مثال:

```csharp
internal enum GroupStatus
{
    Draft,
    Active,
    Completed,
    Cancelled,
    Archived
}
```

لكن نفصل بين:

```text
GroupStatus
RegistrationStatus
GroupVisibility
```

ممنوع قيمة مثل:

```text
OpenForRegistration
```

تتحط داخل GroupStatus لو هي مفهوم منفصل.

---

نستخدم Lookup Table / Entity عندما القيم:

```text
Admin-configurable
Localizable
Have metadata
Institution-specific
Not stable code constants
```

---

# 31. Rich Domain Areas

أجزاء تستحق Rich Domain Model بوضوح:

```text
Seat reservation / last-seat protection

Enrollment Capacity

Enrollment lifecycle
Transfer / freeze / withdrawal

Primary teacher replacement

Session cancellation / makeup

Attendance finalize / reopen

Payments
Refunds
Reversals

Cash shift closing / reconciliation

Teacher compensation / settlements

Ownership / representation

Approvals / conflict of interest
```

---

# 32. Simple CRUD Areas

ممكن تظل أبسط:

```text
Reference catalogs

Simple classification data

Simple configuration without workflow

Read-only queries

Lookup tables without significant behavior
```

لكن تظل ملتزمة بـ:

```text
Validation
Authorization
Tenant Isolation
Database Constraints
Audit when required
```

---

# 33. القواعد النهائية

1. نستخدم Pragmatic Rich Domain Model.
2. Business invariants تحمى داخل الـowner الصحيح.
3. Domain Entity لا تستدعي Module أخرى.
4. Cross-module facts تجمعها Application layer.
5. Strong invariant تعبر Modules → نراجع ownership أولًا.
6. Setters private افتراضيًا.
7. State changes من خلال Domain Methods عند وجود behavior.
8. Entity تبدأ في valid state.
9. Factory للإنشاء المعقد فقط.
10. Aggregates صغيرة.
11. Aggregates references غالبًا IDs.
12. Value Objects لما تضيف معنى فعليًا.
13. Money sign rules تحددها الـDomain concept.
14. Domain Service فقط عندما لا يوجد natural owner.
15. Domain Event للBusiness facts المهمة فقط.
16. Domain Event ≠ Integration Event.
17. Events لا تحمي strong synchronous invariant.
18. Expected failure → Result.
19. Unexpected failure → Exception.
20. Collections لا تعرض mutable.
21. Domain لا تعتمد على HttpContext.
22. Domain لا تعتمد على DbContext.
23. لا Generic Repository.
24. لا Repository لكل Entity.
25. لا global IsDeleted.
26. Historical/financial correction بواسطة reversal/adjustment/correction حسب الـDomain.
27. لا giant Base Entity.
28. Persistence attributes لا تدخل Domain افتراضيًا.
29. Query projections يمكنها استخدام EF Core مباشرة.
30. CRUD البسيط يظل بسيطًا.
31. Domain Entities تظل internal حسب T04.
32. Handler تنظم Use Case ولا تصبح owner لكل rule.
33. Enrollment Capacity وSeat consumption مملوكة لـEnrollments.
34. StudyGroup لا تملك Enrollment seat invariant.

---

# حاجات T06 لا تحسمها

تتحدد في Decisions أخرى:

```text
Final aggregate boundaries

Transactions
→ T15

Concurrency / locks / versions
→ T16

Idempotency
→ T17

Cross-module contracts
→ T18

Domain event dispatch
Integration events
Outbox
→ T19

Exact Result implementation

Multi-tenancy
→ T09

Database constraints
→ T08 / feature design

Exact enum storage

Exact IDs
→ T10
```

---

# القرار النهائي المختصر

> EduCenterOS تستخدم Pragmatic Rich Domain Model: نضع behavior والقواعد داخل الـEntities/Aggregates عندما يكون لها owner طبيعي، ونبقي CRUD البسيط بسيطًا.

> الـApplication Handlers تنظم الـUse Cases، وتقرأ facts من Modules أخرى عند الحاجة، لكن الـDomain نفسها لا تعرف DbContext أو HttpContext أو Module أخرى.

> Strong invariants يجب أن تعيش بجوار state التي تتحكم فيها. لذلك `EnrollmentCapacity + SeatReservation + Enrollment` كلها داخل `Enrollments`، ولم تعد `StudyGroup` تملك `ChangeEnrollmentCapacity()` المعتمدة على counts خارجية.

> نستخدم Aggregates صغيرة وValue Objects وDomain Services وDomain Events عند وجود قيمة فعلية فقط، ولا نفرض Generic Repositories أو Soft Delete أو DDD patterns على كل جزء في النظام.
