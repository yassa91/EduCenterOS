# T03 - Business Modules & Boundaries

## الهدف من القرار

تحديد حدود واضحة لكل Business Area داخل EduCenterOS، بحيث:

- كل Business Concept يكون له Owner واضح.
- كل Module تملك بياناتها وقواعدها.
- نقلل التداخل بين أجزاء النظام.
- نمنع الـModular Monolith من التحول إلى Monolith عشوائي.
- تكون الـModule boundaries متوافقة مع الـBusiness Decisions.
- تكون الـTransactions والـConcurrency قابلة للحماية داخل Ownership صحيحة.
- نمنع تكرار نفس المفهوم أو نفس Source of Truth في أكثر من Module.

---

# القرار النهائي

Core V1 تعتمد 15 Logical Business Modules:

```text
IdentityAccess
Institutions
Subscriptions
Academic
Students
Enrollments
SessionsAttendance
StudentFinance
BranchFinance
TeacherCompensation
Notifications
AuditApprovals
Reporting
Imports
PlatformAdministration
```

Phase 2:

```text
Marketplace
MarketplaceFinance
```

دي Logical Business Modules.

مش معنى كده إن كل Module لازم تكون:

```text
Microservice
Separate deployment
Separate database
Separate process
```

التقسيم الفيزيائي يتبع T04 وT08.

---

# 1. IdentityAccess

مسؤولة عن:

> الحساب، هوية الشخص، Authentication security state، والجلسات.

تملك مفاهيم مثل:

```text
UserAccount
PersonIdentity

AccountStatus
SecurityVersion

Phone Verification
Email Verification
OtpChallenge
VerificationProof

Password Credential State

UserSession
Refresh Credential Records

MfaMethod
RecoveryCode
TrustedDevice
```

حسب الـFeatures المنفذة فعليًا.

مسؤولياتها:

```text
Account registration
Login
Logout
Password security
OTP
Verification
Session management
Account recovery
MFA security state
Contact verification
Person identity at platform level
```

لا تملك:

```text
InstitutionMembership
Institution Role
Branch Scope

StudentProfile
InstitutionStudentRecord
GuardianRelationship

Enrollment
Teacher institutional assignment
```

قاعدة مهمة:

> UserAccount تمثل وسيلة الدخول والحالة الأمنية، وليست "نوع الشخص".

لا يوجد:

```text
AccountMode
GeneralMode
StudentMode
```

Compatible roles/profiles/relationships يمكن أن تتعايش حسب Business eligibility.

---

# 2. Institutions

مسؤولة عن المؤسسة نفسها، الفروع، والـinstitutional access.

تملك:

```text
Institution
Branch

InstitutionMembership
MembershipInvitation

Role
MembershipScope
EnabledCapability

PrimaryOwnerAssignment
AuthorizedRepresentativeAssignment

LegalEntity
BillingProfile
CollectionProfile

InstitutionPublicProfile
InstitutionVisibility
```

ومفاهيم المؤسسة التشغيلية القريبة منها.

مسؤولياتها:

```text
Create/manage institution
Main branch
Branch management

Institution memberships
Invitations

Role / scope / capabilities

PrimaryOwner
AuthorizedRepresentative

Legal entity information
Billing / collection profile

Institution visibility
Public profile
Institution lifecycle
```

قاعدة مهمة:

> Institution membership ملك `Institutions` وليست IdentityAccess.

---

# 3. Subscriptions

مسؤولة عن العلاقة التجارية بين المؤسسة وEduCenterOS.

تملك:

```text
SubscriptionPlan
PlanEntitlement

InstitutionSubscription
Trial

SubscriptionInvoice
SubscriptionPayment

UsageSnapshot
LimitViolation
SubscriptionOverride
```

مسؤولياتها:

```text
Trial
Plan selection
Entitlement Matrix

Student limits
Branch limits
Storage limits

Renewal
Expiration
Usage enforcement
Read-only subscription state when applicable
```

الأرقام التجارية مثل:

```text
Trial = 30 days
Student limits
Branch limits
Storage quotas
Annual pricing
```

تظل:

```text
Configurable
NEEDS_VALIDATION
```

وليست Hard-coded Domain truths.

لا تملك:

```text
Student payments
Branch cash
Teacher settlements
Marketplace earnings
```

---

# 4. Academic

مسؤولة عن:

> ما هي المجموعة أكاديميًا وكيف تعمل كجزء من الهيكل الأكاديمي.

تملك:

```text
EducationSystem
Stage
Grade
AcademicTrack
ReferenceSubject
InstitutionGradeSubject

AcademicYear
AcademicPeriod
AcademicCalendarEvent

Room

StudyGroup
GroupSchedulePattern

PrimaryTeacherAssignment
GroupTeacherHistory
```

مسؤولياتها:

```text
Education reference structure
Institution subject/grade offerings

Active/Draft academic years
Academic periods/calendar

Rooms

StudyGroup academic definition
Primary teacher
Permanent teacher replacement/history
Weekly schedule pattern

Teacher/room schedule conflicts
Academic lifecycle of group
Group visibility
Group codes

Physical / academic constraints
```

Academic يمكن أن تملك Facts مثل:

```text
RoomPhysicalCapacity
Allowed academic group limit
Group academic status
```

لكنها لا تملك:

```text
EnrollmentCapacity
Seat availability
Seat reservations
Student enrollment
Waitlist
```

قاعدة مهمة:

> `StudyGroup` ليست Seat Inventory.

---

# 5. Students

مسؤولة عن Student identity/profile والعلاقة مع المؤسسة وGuardian.

تملك:

```text
StudentProfile
InstitutionStudentRecord
StudentIdentityLink

GuardianRelationship
GuardianVerification / Assurance State when applicable

StudentAcademicStatus
StudentInternalNote
```

مسؤولياتها:

```text
Platform-level StudentProfile
Institution-specific student record

Student without login
Linking account to existing student record

Guardian relationships
Institution-scoped / platform-level guardian assurance where applicable

Student current academic information
Institution student notes/status
Identity duplicate prevention
```

لا يوجد:

```text
StudentMode
PrimaryGuardian based only on school stage
```

Guardian rules تعتمد على:

```text
Age
Eligibility
Assurance
Consent / risk
Requested action
```

وليس School Stage وحدها.

وجود `StudentProfile` لا يمنع:

```text
InstitutionMembership
GuardianRelationship
other compatible roles
```

---

# 6. Enrollments

مسؤولة عن:

> رحلة الطالب من الرغبة في المقعد إلى الحجز والتسجيل والنقل والتجديد.

تملك:

```text
Booking

GroupEnrollmentPolicy / GroupSeatInventory

EnrollmentCapacity
SeatReservation

WaitlistEntry
WaitlistOffer

Enrollment
EnrollmentHistory

Renewal
Transfer
Freeze
Withdrawal

EarlyBooking
```

الأسماء النهائية لبعض Aggregates مثل:

```text
GroupEnrollmentPolicy
GroupSeatInventory
```

تتحدد أثناء تنفيذ الـModule.

مسؤولياتها:

```text
Booking
Seat reservation
Final enrollment

Waitlist
Waitlist offers

Renewal
Transfer
Freeze
Withdrawal

Early booking for future Draft year

Enrollment lifecycle
Seat availability
Enrollment Capacity
```

---

# 7. Seat Capacity Ownership

الـBusiness invariant:

```text
OccupiedCapacity
<=
EnrollmentCapacity
```

حيث:

```text
OccupiedCapacity
=
Confirmed Enrollments
+
Active Seat Reservations
+
Any explicitly policy-counted states
```

كل هذه الـstate مملوكة لـ:

```text
Enrollments
```

لذلك:

```text
EnrollmentCapacity
→ Enrollments
```

وليس `Academic`.

أي operation تغير:

```text
OccupiedCapacity
or
EnrollmentCapacity
```

تمر عبر نفس consistency/concurrency boundary طبقًا لـT15/T16.

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

# 8. Academic Physical Limit vs Enrollment Capacity

نفرق بين مفهومين:

```text
Physical / Academic Limit
→ Academic

Enrollment Capacity
→ Enrollments
```

مثال:

```text
RoomPhysicalCapacity = 30
```

ممكن تكون Fact من Academic.

لكن المؤسسة قد تختار:

```text
EnrollmentCapacity = 24
```

داخل Enrollments.

وقد تطبق قاعدة:

```text
EnrollmentCapacity
<=
Allowed Academic / Physical Limit
```

لكن ده لا ينقل Seat ownership إلى Academic.

Enrollments تقرأ Academic facts عن طريق Contracts حسب T18.

وقت كتابة المقاعد، Enrollments تعتمد على `MaxAllowedEnrollmentCapacitySnapshot` محلية ومُرقّمة داخل `GroupSeatInventory`، وليست على Live read قابلة للتقادم. في الخفض تقبل Enrollments القيد أولًا قبل تفعيله في Academic؛ وفي الرفع تفعل Academic الحد الأعلى أولًا قبل أن تتبناه Enrollments. التفاصيل في T15، وشكل العقد في T18.

ولا تعمل:

```text
Enrollments
→ lock Academic StudyGroup
→ reserve seat
```

---

# 9. Waitlist Rules

Waitlist:

```text
≠ Enrollment
≠ SeatReservation
≠ Guaranteed Seat
```

ولا تستهلك Capacity.

السياسات الأساسية:

```text
BroadcastFirstCome
OrderedOffers
```

`OrderedOffers` يمكن أن تستخدم FIFO baseline وOffer Expiry.

ممنوع Auto-enrollment أو Auto-transfer من Waitlist في Core V1.

تحويل:

```text
Waitlist Offer
→ SeatReservation
```

هو النقطة التي تبدأ فيها Seat Capacity consumption.

---

# 10. SessionsAttendance

مسؤولة عن الحصص الفعلية والحضور.

تملك:

```text
ClassSession
SessionOccurrence

AttendanceRecord
AttendanceCorrection

SessionCancellation
SessionReschedule
MakeupSession

SessionClosure / Finalization
AttendanceReopen when allowed

OfflineAttendanceOperation / Sync state when implemented
```

مسؤولياتها:

```text
Generate actual sessions from schedule patterns

Attendance
Absence
Lateness

Attendance finalization
Correction
Reopen according to rules

Cancellation
Rescheduling
Makeup sessions

Temporary teacher delivery
Offline attendance sync
```

`ClassSession` تحفظ حسب الحاجة:

```text
ExpectedTeacher
DeliveredByTeacher
```

Temporary substitute لا تغير PrimaryTeacher داخل Academic.

Permanent teacher replacement يتم في Academic.

---

# 11. StudentFinance

مسؤولة عن العلاقة المالية بين الطالب والمؤسسة.

تملك مفاهيم مثل:

```text
StudentFinancialAccount

Charge
ChargeAdjustment
ChargeReversal

Payment
PaymentAttempt
PaymentAllocation
PaymentReversal

Discount
Credit

RefundRequest
Refund

MigrationOpeningReceivable
MigrationOpeningCredit
```

مسؤولياتها:

```text
What the student owes
Payments
Payment allocations

Discounts
Credits

Outstanding receivables
Refunds
Reversals

Electronic payment workflow
Opening financial migration values
```

قاعدة مهمة:

```text
Balance
→ Derived
```

ولا يتم تعديل Balance مباشرة.

الحركات المالية historical/immutable-ish:

```text
No delete/edit history
Correction through:
Reversal
Adjustment
Refund
```

---

# 12. BranchFinance

مسؤولة عن:

> أين تحركت الأموال فعليًا داخل المؤسسة والفروع.

تملك:

```text
Safe
CashDrawer

CashShift
CashCount
CashDifference

CashMovement
SafeTransfer

Expense
ExpenseCategory
Reimbursement

ShiftClosing
SafeReconciliation
```

مسؤولياتها:

```text
Safes
Cash handling

Cashier/employee shifts
Opening/closing counts

Physical cash movements
Expenses

Transfers
Reconciliation
Cash differences
```

الفرق:

```text
StudentFinance
→ الطالب دفع مقابل ماذا؟

BranchFinance
→ الفلوس دخلت/خرجت من أين ومن كان مسؤولًا عنها؟
```

قاعدة مهمة:

```text
Payment
≠
CashMovement
```

لكن `RecordCashPayment` تحتاج الاثنين أو لا شيء: T15 تعتمد لها Same-database atomic exception واحدة. StudentFinance تنشئ `Payment` عبر participant تملكها، وBranchFinance تتحقق من الـCashShift/Drawer وتنشئ `CashMovement` عبر participant تملكها، داخل PostgreSQL transaction مشتركة بدون Direct cross-module writes أو مشاركة DbContexts.

---

# 13. TeacherCompensation

مسؤولة عن العلاقة المالية والتعاقدية بين المؤسسة والمدرس.

تملك:

```text
TeacherContract
TeacherContractVersion

CompensationRule
TeacherEarning
TeacherAdjustment

TeacherSettlement
SettlementDispute

TeacherPayment
```

ومفاهيم Approval اللازمة حسب ownership.

مسؤولياتها:

```text
Teacher contracts
Effective-dated contract versions

Compensation rules
Teacher earnings

Adjustments
Settlements
Disputes
Payments
```

Approval/Audit orchestration تتكامل مع `AuditApprovals`.

لا تخلط مع:

```text
Marketplace teacher earnings
```

---

# 14. Notifications

مسؤولة عن توصيل الرسائل وليس اتخاذ Business decisions.

تملك:

```text
Notification
NotificationRecipient

NotificationPolicy
NotificationTemplate
NotificationPreference

DeliveryAttempt
DeliveryStatus

NotificationChannel
NotificationDeduplication
```

مسؤولياتها:

```text
In-app notifications
Email
SMS when introduced

Templates
Preferences

Delivery status
Retries
Deduplication
```

قاعدة مهمة:

> Notifications لا تقرر إن الحدث يجب أن يحدث؛ الـBusiness Module المالكة هي التي تقرر.

---

# 15. AuditApprovals

مسؤولة عن الرقابة، الوصول الحساس، والـapproval workflows.

تملك:

```text
AuditRecord
SensitiveAccessLog

ApprovalRequest
ApprovalStep
ApprovalDecision

RelatedPartyMarker
ExceptionalOperation

BreakGlassAccessRecord when implemented
```

مسؤولياتها:

```text
Audit sensitive operations

Sensitive data access logging

Approval workflows
Maker-checker rules

Related-party/conflict handling
Exceptional operation tracking
```

قاعدة مهمة:

> AuditApprovals تدير approval state، لكن الـBusiness Module الأصلية هي التي تنفذ العملية الفعلية بعد الاعتماد.

---

# 16. Reporting

مسؤولة عن:

> القراءة والتحليل فقط.

تملك:

```text
ReadModels
DashboardProjections
KpiSnapshots

ReportDefinitions
GeneratedReports
ExportJobs
```

مسؤولياتها:

```text
Dashboards
Reports
KPIs
Read models
Exports
Analytical projections
```

قاعدة مهمة:

```text
Reporting
≠ Source of Truth
```

ولا تكتب Business state داخل Modules أخرى.

---

# 17. Imports

مسؤولة عن Migration/Import workflow.

تملك:

```text
ImportJob
ImportFile

ImportTemplateVersion
ImportMapping

ImportStaging
ImportValidationResult
ImportRowError

ImportPreview
ImportExecutionBatch

ImportReconciliation
ImportReport
```

مسؤولياتها:

```text
Secure file intake
Parsing
Staging
Mapping
Validation

Duplicate/conflict detection
Dry run / Preview
Confirmation

Execution
Reconciliation
Reporting
```

قاعدة مهمة:

> Imports لا تعدل Tables الخاصة بالـModules مباشرة.

بل:

```text
Imports
→ Owner Module Contracts / Commands
```

Import لا تنشئ UserAccount تلقائيًا للطالب.

Imported Guardian relationship لا تصبح `PlatformVerified` تلقائيًا.

Passwords/MFA/session secrets لا يتم استيرادها.

---

# 18. PlatformAdministration

مسؤولة عن إدارة EduCenterOS نفسها.

تملك:

```text
PlatformRole / PlatformAuthorization concepts

PlatformCase
SupportCase

InstitutionComplaint
ModerationAction

InstitutionSuspension
VerificationCase
PublicListingVerification

RiskFlag
AbuseCase

Trial / subscription exceptional review when applicable

PlatformConfiguration
```

مسؤولياتها:

```text
Platform support
Complaints

Institution verification/listing verification
Restrictions / suspension

Abuse handling
Risk flags
Exceptional cases

Control/ownership disputes
Platform configuration
```

قاعدة مهمة:

> Platform Admin لا تعدل Business tables يدويًا ولا تحصل على permanent AllTenants bypass.

كل عملية تكون:

```text
Explicit use case
Authorized
Scoped
Audited
```

---

# Phase 2 — Marketplace

Marketplace ليست Core V1.

لا ننشئ لها:

```text
Project
Schema
DbContext
Migrations
Endpoints
```

قبل بدء Phase 2 فعليًا.

عند تنفيذها ستملك مثلًا:

```text
IndependentTeacherProfile

Course
Lecture
ContentItem
VideoAsset
CourseFile

Quiz
CourseAccess
LearningProgress
CourseReview

ContentPublication
```

يمكن أن تشارك Platform Identity، لكن Domain/financial ledgers الخاصة بها مستقلة عن Institution operations.

---

# Phase 2 — MarketplaceFinance

مسؤولة مستقبلًا عن:

```text
MarketplaceOrder
MarketplacePayment
MarketplaceRefund

PlatformCommission
GatewayFee

TeacherEarning
TeacherPayoutRequest
TeacherPayout
```

قاعدة مهمة:

```text
MarketplaceFinance
≠ StudentFinance
≠ BranchFinance
≠ TeacherCompensation
≠ Subscriptions
```

كل Ledger لها Ownership مستقلة.

---

# حاجات مش Business Modules

دي Building Blocks / Infrastructure:

```text
Clock

Result
Error

CorrelationId

Encryption

FileStorage

EmailSender
SmsSender
PaymentProviderClient

EventDispatcher

Outbox Infrastructure

Database Transaction primitives

Logging
```

ممنوع إنشاء:

```text
Common
SharedBusiness
Utils
```

ونرمي فيها أي Concept مش عارفين Owner بتاعه.

لو Concept Business مش واضح Owner بتاعه:

> نراجع الـDomain boundaries.

---

# قواعد حدود الـModules

1. كل Business Concept له Owner واحد واضح.
2. كل Module تكتب فقط في Tables التي تملكها.
3. ممنوع استخدام DbContext الخاصة بـModule أخرى.
4. ممنوع Cross-Module Navigation Properties.
5. Cross-Module references تكون غالبًا IDs.
6. التواصل يكون عبر:

```text
Public Contracts
Queries / Facts
Application Commands when explicitly justified
Domain Events
Integration Events
Read Models
```

7. ممنوع Circular Module Dependencies.
8. Cross-Module synchronous commands استثناء وليست default.
9. لا Cross-Module shared transaction كحل طبيعي؛ `RecordCashPayment` هي الاستثناء الوحيد المعتمد حاليًا بشروط T15.
10. لو Strong Invariant تحتاج atomic state من أكثر من Module:

```text
Review ownership first
```

11. Reporting Read-only.
12. Notifications لا تملك Business decision.
13. Imports تستخدم Owner Module contracts.
14. PlatformAdministration لا تعمل direct table editing.
15. BuildingBlocks لا تحتوي Business Entities أو Business Services.
16. Cross-Module FK طبقًا لـT08:

```text
Prohibited by default
Explicit stable structural FK may be allowed
```

لكن FK لا تغير ownership ولا تسمح Direct navigation/write.

---

# Seat Ownership Rule

قرار مهم مرتبط بـT15/T16:

```text
Academic
→ StudyGroup academic definition
→ schedule
→ teacher
→ room
→ academic/physical limits

Enrollments
→ EnrollmentCapacity
→ SeatReservation
→ Enrollment
→ Waitlist
→ Seat availability
```

وبذلك:

```text
OccupiedCapacity <= EnrollmentCapacity
```

تظل Local Strong Invariant داخل `Enrollments`.

---

# حاجات T03 لا تحسمها

يتم حسمها في Decisions مستقلة:

```text
Physical project structure
→ T04

API style / slices
→ T05

Domain modeling
→ T06

Database schemas / DbContexts / migrations / FKs
→ T08

Multi-tenancy
→ T09

Transactions
→ T15

Concurrency
→ T16

Idempotency
→ T17

Synchronous module communication
→ T18

Events / Outbox
→ T19

Exact aggregate boundaries
→ Feature design
```

---

# القرار النهائي المختصر

> EduCenterOS Core V1 مقسمة إلى 15 Business Modules واضحة، وكل Module تملك بياناتها وقواعدها ولا تعدل بيانات Module أخرى مباشرة.

> IdentityAccess تملك الحساب والأمان فقط؛ Institutions تملك institutional access؛ Students تملك Student/Guardian relationships؛ Enrollments تملك رحلة الحجز والتسجيل وسعة المقاعد التشغيلية؛ StudentFinance وBranchFinance وTeacherCompensation Ledgers منفصلة.

> `StudyGroup` والـRoom والـschedule والـteacher والقيود الأكاديمية/الفيزيائية تظل في Academic، بينما `EnrollmentCapacity + SeatReservation + Enrollment` والنسخة المقبولة والمُرقّمة من الحد الأكاديمي تظل في Enrollments حتى يمكن حماية Seat invariant داخل Transaction/Concurrency boundary واحدة.

> `Payment` و`CashMovement` يظلان مفهومين وملكية منفصلين، لكن `RecordCashPayment` هي named atomic exception الوحيدة في Core V1 حسب T15، بدون كسر ownership.

> Marketplace وMarketplaceFinance مؤجلتان إلى Phase 2 ولا يتم إنشاء artifacts فيزيائية لهما في Core V1.
