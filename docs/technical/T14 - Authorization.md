# T14 - Authorization

## القرار

EduCenterOS يعتمد:

```text id="zn4vot"
Server-side Authorization
+
Authenticated ICurrentActor
+
Validated Institution Context
+
Explicit Access Relationship
+
One Explicit Access Path per Authorization Decision
+
Fixed Staff Roles
+
Branch Scope
+
Optional Capabilities
+
Stable Permission Codes
+
Resource-aware Authorization
+
Eligibility / Conflict / Approval Rules
+
Authentication Assurance Requirements
+
Fail-Closed Tenant Authorization
```

ولا يعتمد على:

```text id="0vk9h9"
JWT Role Claims as Source of Truth
AccountMode
StudentMode
[Authorize(Roles = ...)]
Client-provided authorization state
Global superuser bypass
Permission union across Staff / Student / Guardian relationships
Generic relationship IDs trusted from the client
```

---

# 1. Authentication vs Authorization

T13 تجيب:

```text id="pyk518"
Who is the actor?
Is the session valid?
What Authentication Assurance exists?
```

T14 تجيب:

```text id="vm7p1x"
What can this actor do now?
Through which relationship?
Inside which institution/scope?
On which resource?
Under which eligibility/security/approval rules?
```

---

# 2. General Authorization Formula

القرار العام:

```text id="x61bpa"
Account Status
+
Active Access Relationship
+
Relationship Status
+
Role / Scope / Capabilities when applicable
+
Requested Resource
+
Eligibility
+
Conflict Rules
+
Approval Rules
+
Authentication Assurance
↓
Allow / Deny
```

مش كل Access Kind تستخدم كل العناصر.

---

# 3. ICurrentActor

تظل كما اعتمدنا في T13:

```csharp id="iz00vv"
public interface ICurrentActor
{
    Guid UserAccountId { get; }

    Guid UserSessionId { get; }
}
```

ولا تحتوي:

```text id="wmhzpy"
InstitutionId
Role
MembershipId
BranchScope
Capabilities
StudentProfileId
GuardianRelationshipId
Authentication Assurance
```

وظيفتها فقط:

> من هو الـActor المصادق عليه؟

---

# 4. Institution Context

أي Institution تأتي من:

```text id="wzxkvk"
Route
Header
Query
Body
Workspace selection
```

تعتبر:

```text id="9a9o33"
Requested Institution Candidate
```

وليست Authorization.

بعد Server-side validation يتم إنشاء:

```text id="3hqx5x"
Validated IInstitutionContext
```

---

# 5. IInstitutionContext

الـContext العامة لا نفترض أن كل Access Kind لديها Role.

الشكل المشترك يظل صغيرًا:

```csharp id="1u0r2m"
public interface IInstitutionContext
{
    Guid InstitutionId { get; }

    Guid UserAccountId { get; }

    InstitutionAccessKind AccessKind { get; }
}
```

ولا نضع `AccessRelationshipId` عامة يمكن تفسيرها بأكثر من معنى. كل Access path تستخدم Context typed:

```csharp
public interface IStaffInstitutionContext : IInstitutionContext
{
    Guid InstitutionMembershipId { get; }
}

public interface IStudentInstitutionContext : IInstitutionContext
{
    Guid StudentProfileId { get; }
    Guid InstitutionStudentRecordId { get; }
}

public interface IGuardianInstitutionContext : IInstitutionContext
{
    Guid GuardianRelationshipId { get; }
    Guid InstitutionStudentRecordId { get; }
}

public interface IPlatformSupportInstitutionContext : IInstitutionContext
{
    Guid PlatformCaseId { get; }
    Guid ExceptionalAccessGrantId { get; }
}
```

الأشكال Conceptual؛ أما اتجاه الـContracts ومكان الـinterfaces النهائي فيحسمه T18 بدون Circular Dependencies.

الـContext:

```text id="arw9f8"
Scoped
Immutable
Server-validated
Fail-closed
```

ولا تنشأ من Request DTO مباشرة.

لا تتغير `InstitutionId` أو `AccessKind` داخل نفس Operation scope. لو Use Case موثقة تحتاج فحص علاقة إضافية، يتم فحصها كـresource/business condition صريحة بدون تبديل Context أو دمج permission bags.

T14 هي التعريف المرجعي النهائي لشكل Institution Context، وT09 تشير إلى هذا الشكل بدل الاحتفاظ بنسخة Authorization snapshot داخل Context العامة.

---

# 6. InstitutionAccessKind

القيم المفاهيمية:

```text id="3c0c14"
StaffMembership
Student
Guardian
PlatformSupport
```

ولا يوجد:

```text id="uuxrc0"
Parent
StudentMode
GeneralMode
AccountMode
```

وجود AccessKind في التصميم لا يعني تنفيذ Feature قبل وقتها.

---

# 7. Access Relationship

كل وصول مؤسسي يجب أن يكون له سبب فعلي.

مثل:

```text id="mugl10"
Staff
→ InstitutionMembership

Student
→ InstitutionStudentRecord
  + linked StudentProfile when required

Guardian
→ GuardianRelationship
  + linked InstitutionStudentRecord

PlatformSupport
→ explicit Support / Investigation authorization
```

كل Typed relationship ID مجرد Reference وليست Secret ولا تمنح Authorization وحدها.

## 7.1 Actor Binding

كل Relationship يجب إثبات أنها تخص الـActor المصادق عليه، وليس فقط أنها موجودة داخل المؤسسة:

```text
CurrentActor.UserAccountId
↓ IdentityAccess contract
CurrentActor.PersonIdentityId
```

ثم حسب Access Kind:

```text
Staff
→ Membership subject is the current PersonIdentity/UserAccount

Student
→ StudentProfile is linked to current PersonIdentity
→ InstitutionStudentRecord belongs to that StudentProfile + Institution

Guardian
→ GuardianRelationship guardian identity is current PersonIdentity
→ Relationship covers the requested student + Institution + action

PlatformSupport
→ Exceptional grant belongs to current platform actor/case + Institution
```

أي Relationship ID قادمة من Client هي Candidate فقط. معرفة ID صحيحة لا تمنح Access.

## 7.2 One Access Path per Decision

نفس الشخص قد يكون داخل نفس المؤسسة:

```text
Staff
+
Student
+
Guardian
```

لكن كل Authorization decision تختار Access path واحدة صريحة ومتحققًا منها Server-side.

ممنوع:

```text
Staff permissions
+
Guardian relationship
+
Student self-access
→ one merged permission bag
```

الـAccess path تحددها طبيعة Endpoint/Use Case، أو Requested Workspace/AccessKind كـCandidate يتحقق منها السيرفر. لا تعمل fallback تلقائيًا لمسار آخر بعد Deny، ولا تجمع صلاحيات من مسارات مختلفة إلا Use Case موثقة صراحة لها Business rule محددة.

---

# 8. من يتحقق من العلاقة؟

الـModule المالكة للعلاقة تظل Source of Truth.

```text id="nz3xsc"
Institutions
→ InstitutionMembership

Students
→ StudentProfile
→ InstitutionStudentRecord
→ GuardianRelationship

PlatformAdministration
→ exceptional platform access
```

T14 لا تسمح لـ`Institutions` بقراءة Tables الخاصة بـStudents مباشرة.

شكل الـcross-module contracts النهائي يتبع T18 مع منع Circular Dependencies.

---

# 9. Staff Authorization

الوصول الوظيفي يعتمد على:

```text id="6ne7gc"
Active InstitutionMembership
+
Fixed Role
+
Branch Scope
+
Enabled Capabilities
+
Required Permission
```

ثم تضاف حسب العملية:

```text id="lx2ov7"
Resource-specific relationship
Eligibility
Authentication Assurance
Conflict / Approval rules
```

---

# 10. Fixed Institution Roles

Core V1 تستخدم Roles ثابتة من المنصة، مثل:

```text id="yuakvp"
PrimaryOwner
AuthorizedRepresentative
InstitutionManager
BranchManager
Receptionist
Accountant
Teacher
AttendanceOfficer
```

المؤسسة لا تنشئ Arbitrary Roles أو Permissions في V1.

كل `InstitutionMembership` تحمل Role واضحة حسب النموذج المعتمد.

---

# 11. أشياء ليست Institution Roles

```text id="8av8yw"
Student
Guardian
IndependentTeacher
PlatformAdministrator
SupportAgent
OnboardingIntent
```

كل واحدة لها Access Model مستقلة.

---

# 12. Permission

`Permission` تمثل عملية Atomic يفحصها الـBackend.

مثل:

```text id="ubx8fr"
academic.study_groups.create
students.records.view
enrollments.bookings.create
student_finance.cash.record
student_finance.refunds.approve
branch_finance.safes.reconcile
teacher_compensation.settlements.approve
```

Permission Code:

```text id="dovnre"
Stable identifier
Platform-defined
Not a security secret
```

---

# 13. Capability

`Capability` اختيار Platform-defined يمكن تفعيله لعضوية عندما يسمح Role بذلك.

مثال:

```text id="l83rk4"
reception.cash_collection
```

قد تمنح Permission أو مجموعة صغيرة مترابطة.

لكن:

> Capability لا تتجاوز الحد الأقصى المسموح للدور.

---

# 14. Effective Staff Permissions

مفهوميًا:

```text id="58w2vo"
Role.CorePermissions
+
Permissions granted by EnabledCapabilities
-
Role.ForbiddenPermissions
```

`ForbiddenPermissions` لها الأولوية.

Membership لا تخزن قائمة Permissions عشوائية يكتبها المستخدم.

---

# 15. Permission Catalog

`Institutions` تملك Authorization vocabulary والمصفوفة الخاصة بالأدوار والCapabilities.

الـCatalog المركزية تحتوي Metadata ثابتة Platform-defined وليست بيانات يكتبها مستخدم.

قبل تنفيذ أي Module تدخل نطاق مشروع التخرج، يجب أن يكون لكل Permission سجل موثق يحتوي على الأقل:

```text
Code
OwningModule
Description
AllowedRoles
GrantedByCapabilities?
ForbiddenRoles
ScopeKind                  // Institution / Branch / Resource / Self
RequiresMfa
RequiresRecentAuthentication
StepUpPurpose?
StepUpSingleUse?
RequiresIndependentApproval
SensitiveDataAccess
```

الـPermission Matrix تحفظ كـversion-controlled code/configuration داخل الحل، وليست rows قابلة للتعديل من المؤسسة في V1.

كل Module تملك تعريف أسماء العمليات التي تعرضها، بينما `Institutions` تملك Role/Capability matrix التي تشير إلى هذه identifiers. اتجاه الـcompile-time contracts والتجميع النهائي يتبع T18 ولا يسمح لـInstitutions باستدعاء DbContext أي Module أخرى.

Startup/build-time validation تفشل عند وجود:

```text
Duplicate permission code
Role/capability references unknown permission
Same permission both granted and forbidden for the same role
Capability grants beyond the role maximum
Sensitive permission without explicit assurance/approval metadata decision
Protected endpoint/use case without declared authorization requirement
```

أي Endpoint عامة تعلن `Public` صراحة؛ غياب Permission metadata لا يعني السماح الافتراضي.

أما معنى العملية وقواعدها الفعلية فتظل في الـBusiness Module المالكة.

يعني:

```text id="1d5g8w"
Permission says:
student_finance.refunds.approve

StudentFinance decides:
هل Refund نفسها في حالة تسمح بالApproval؟
```

---

# 16. Authorization ≠ Business Rule

وجود Permission لا يكفي لتنفيذ العملية.

مثلًا:

```text id="3b8134"
Permission:
academic.study_groups.change_capacity
```

لا يلغي:

```text id="g86ex4"
Capacity business rules
Concurrency rules
Seat inventory rules
```

الـDomain والـApplication rules تظل مطبقة.

كذلك Subscription/Plan entitlement ليست Permission:

```text
Permission
→ هل هذا الـActor مخول بتنفيذ العملية؟

Plan Entitlement / Subscription State
→ هل الخدمة متاحة للمؤسسة أصلًا؟
```

العملية التي تحتاج الاثنين يجب أن تتحقق منهما، ولا يؤدي وجود أحدهما إلى افتراض الآخر.

---

# 17. Branch Scope

للـStaff Membership نستخدم:

```text id="w2bj52"
InstitutionWide
SelectedBranches
```

`SelectedBranches` يمكن أن تحتوي فرعًا واحدًا أو أكثر.

ولا يوجد:

```text id="oe1erg"
SelectedBranches + []
→ All branches
```

Empty selection لا توسع الصلاحية.

---

# 18. Branch Authorization

حسب T09:

```text id="uh940d"
Institution Isolation
→ baseline tenant protection

Branch Scope
→ explicit authorization
```

ولا نستخدم Global Branch Query Filter لكل النظام.

أي BranchId قادمة من Client Candidate فقط.

يجب إثبات:

```text id="a9j0bq"
Branch belongs to Institution
+
Actor has access to Branch
```

---

# 19. Resource-specific Authorization

BranchScope ليست كافية دائمًا.

مثال Teacher:

```text id="otrvds"
Valid Staff Membership
+
Teacher Role/Permission
+
Branch allowed
+
Teacher assigned to StudyGroup
```

`TeacherAssignment` تظل مملوكة لـAcademic.

ولا نحول Institution Context إلى Object يحمل كل Assignments في النظام.

---

# 20. Student Access

لا يوجد `StudentMode`.

Student authorization تعتمد على:

```text id="ypc9kx"
Authenticated UserAccount
+
Linked StudentProfile
+
InstitutionStudentRecord
+
Requested Resource
+
Eligibility
```

وقد تدخل عوامل أخرى حسب العملية.

وجود `StudentProfile` وحدها لا يمنح الوصول لكل مؤسسة.

ووجود Student identity لا يمنع Staff أو Guardian relationships متوافقة.

---

# 21. Guardian Access

Guardian authorization تعتمد على:

```text id="5yr3qp"
GuardianRelationship
+
Relationship Status
+
Relationship Scope
+
Assurance Level
+
InstitutionStudentRecord
+
Requested Resource / Action
+
Eligibility
```

ولا تعتمد على Staff Membership.

---

# 22. Guardian Assurance

نسميها صراحة:

```text
GuardianRelationshipAssurance
```

حتى لا تختلط مع `AuthenticationAssuranceLevel` المملوكة لـIdentityAccess.

الـGuardian relationship قد تحمل Assurance مثل:

```text id="0b946e"
InstitutionScoped
PlatformVerified
```

حسب الـBusiness Model.

الـAuthorization لا تفترض أن أي Relationship لها نفس قوة الإثبات.

وقد تتطلب بعض العمليات Assurance أعلى من غيرها.

---

# 23. Guardian Eligibility

صلاحيات Guardian لا تعتمد على School Stage وحدها.

التقييم يعتمد على:

```text id="uz17xt"
Age / eligibility
Relationship
Assurance
Requested action
Consent / risk rules when applicable
```

ولا يوجد Rule عامة تقول:

```text id="9aiud1"
Parent approved
→ allow everything
```

---

# 24. Independent Teacher

`IndependentTeacherProfile` تخص Marketplace Phase 2.

وجودها:

```text id="1kmlpl"
≠ Institution Teacher Membership
```

ولا تمنح وصولًا لمؤسسة.

---

# 25. PrimaryOwner

`PrimaryOwner` هي Primary Platform Controller للمؤسسة، وليست Security bypass.

حتى PrimaryOwner تخضع لـ:

```text id="nnusnk"
Tenant isolation
Business invariants
Authentication Assurance
Sensitive-operation rules
Conflict-of-interest
Approval rules
Audit
```

ممنوع:

```csharp id="nehlr5"
if (role == PrimaryOwner)
    AllowEverything();
```

---

# 26. AuthorizedRepresentative

صلاحيتها تأتي من Delegation فعلية.

ولا تصبح Owner تلقائيًا.

ولا تحصل تلقائيًا على:

```text id="7a9t7z"
Transfer ownership
Disable PrimaryOwner
Close institution unilaterally
Self-grant capabilities
```

أي صلاحية تعتمد على Authorization + Delegation + Business Rules.

---

# 27. Conflict of Interest

Permission لا تتجاوز:

```text id="3ue68f"
Maker-checker
Self-approval restrictions
Related-party rules
```

مثال:

```text id="d4qpne"
Can request refund
+
Can approve refunds
```

لا يعني:

```text id="eqto8h"
Can approve own refund request
```

---

# 28. Approval State

بعض العمليات تحتاج:

```text id="em1s9m"
Permission
+
Valid Approval State
```

Authorization لا تعمل Bypass للـApproval Workflow.

تفاصيل الـworkflow تظل في `AuditApprovals`.

---

# 29. Authentication Assurance

Authentication evidence تأتي من T13 عبر Contract مثل:

```csharp id="rv9gd4"
public interface IAuthenticationContext
{
    DateTimeOffset InitialAuthenticatedAtUtc { get; }

    DateTimeOffset LastPrimaryAuthenticatedAtUtc { get; }

    AuthenticationAssuranceLevel AssuranceLevel { get; }

    DateTimeOffset? MfaSatisfiedAtUtc { get; }

    bool HasActiveMfaMethod { get; }
}
```

دي Security evidence فقط.

ولا تمنح:

```text id="c8exuk"
Role
Permission
Institution access
```

---

# 30. MFA / Recent Authentication

Permission حساسة قد تتطلب Metadata مثل:

```text id="rzp0e9"
RequiresMfa
RequiresRecentAuthentication
RequiresIndependentApproval
SensitiveDataAccess
```

لكن Metadata لا تنفذ Business Workflow بنفسها.

مثال:

```text id="wcr0uj"
Permission allowed
+
MFA required
+
MFA evidence missing
↓
Step-up required
```

بعد Step-up يتم Retry للعملية.

Core V1 تعتبر MFA إلزامية على الأقل عند الوصول من خلال:

```text
PrimaryOwner
AuthorizedRepresentative
Accountant
Platform Administrator / Support exceptional access
```

وكذلك لأي Permission تحمل واحدة من:

```text
Refund / Reversal / Financial Approval
Sensitive permission administration
Money-receipt administration when implemented
Institution control transfer
Sensitive identity/document reveal
Break-glass / exceptional platform access
```

وجود Role حساسة بدون MFA enrollment لا يؤدي إلى bypass. النتيجة تكون `MfaEnrollmentRequired` أو `StepUpRequired` حسب حالة الحساب والعملية، من خلال Error contract الموحد في T32.

حالة `MfaEnrollmentRequired` تسمح فقط بمسارات الأمان اللازمة مثل:

```text
Enroll/confirm MFA
Session listing/revocation
Logout
Approved recovery path
```

ولا تمنح Sensitive institution operation مؤقتًا. وإذا كان نفس الشخص يملك Student/Guardian access مستقلة، تقيم من خلال Access path الخاصة بها بدون استعارة صلاحيات الـRole الحساسة.

---

# 31. Step-up

Step-up:

```text id="rglx2o"
raises Authentication Assurance
```

ولا:

```text id="m6p035"
grants role
grants permission
changes membership
```

مدة Recent Authentication تكون Security Configuration وليست Business constant داخل الـAuthorization code.

التنفيذ يعتمد على `StepUpGrant` المملوكة لـIdentityAccess في T13:

```text
Current UserSession
+
Exact StepUpPurpose
+
TargetResourceId when required
+
Required assurance level
+
Not expired / not consumed
```

ممنوع اعتبار `Session.AssuranceLevel = MultiFactorAuthenticated` تصريحًا عامًا لكل العمليات الحساسة طوال عمر Session.

العمليات الأعلى خطورة، مثل transfer of control أو high-risk refund أو break-glass، تستخدم Grant أحادية الاستخدام وتستهلك داخل IdentityAccess لإصدار تذكرة عملية مقيدة حسب T13/T15.

التنفيذ المعتمد لا يفتح Transaction بين IdentityAccess والـBusiness Module:

```text
IdentityAccess validates + consumes exact StepUpGrant
↓
issues OperationAuthorizationTicket
bound to session + module + operation + target + OperationId
↓
Target Module validates current authorization again
↓
stores TicketId + OperationId atomically with the business mutation
```

الـTicket لا تتجاوز Authorization الحالية ولا تحمل Permission؛ هي تثبت فقط أن Authentication assurance المطلوبة تحققت للنية المحددة. Wrong binding أو expired/revoked session تفشل Fail Closed في First execution. Replay لعملية Completed تعيد Authorization الحالية وفق T17 لكنها لا تتطلب Fresh Ticket أو تعيد تنفيذ العملية.

---

# 32. Authorization Evaluation

الترتيب المفاهيمي:

```text id="om6vix"
Authentication valid?
↓
Account/session valid?
↓
Requested Institution valid?
↓
Required AccessKind/path selected?
↓
Active Access Relationship?
↓
Relationship belongs to current actor/person?
↓
Institution Context established?
↓
Required Permission / allowed operation?
↓
Scope valid?
↓
Resource belongs to Institution?
↓
Resource-specific relationship valid?
↓
Eligibility satisfied?
↓
Authentication Assurance sufficient?
↓
Conflict rules satisfied?
↓
Approval requirements satisfied?
↓
Subscription/feature entitlement satisfied when applicable?
↓
Business state allows operation?
```

مش كل Use Case تحتاج كل خطوة.

---

# 33. ASP.NET Core Authorization Policies

تستخدم للحالات العامة المبنية على Authentication Security Context مثل:

```text id="g7mlgo"
Authenticated
MFA satisfied
Recent authentication
```

لكن لا نستخدم Claims قديمة لحسم:

```text id="cqrbnh"
Institution Role
Branch Scope
Capabilities
Membership Status
```

---

# 34. No Role Claims

ممنوع:

```csharp id="0x1bqv"
[Authorize(Roles = "PrimaryOwner")]
```

كمصدر نهائي للصلاحية.

وممنوع تخزين:

```text id="wrkw43"
role
permissions
capabilities
branch_ids
institution_id
```

في JWT كـAuthorization Source of Truth.

---

# 35. Server-side Authorization State

تغييرات:

```text id="v7idfn"
Membership
Role
BranchScope
Capabilities
Relationship status
```

تظهر في Authorization اللاحقة بدون انتظار JWT expiration.

Institution-scoped changes لا تزيد `Account.SecurityVersion` افتراضيًا.

---

# 36. Platform Administration

Platform Admin لا تحصل على:

```text id="8798bc"
AllTenants = true
```

ولا Permanent Global Institution Context.

الوصول الاستثنائي يحتاج:

```text id="r6sf3q"
Specific Institution
Authorized platform operation
Reason
Case / Investigation reference
Time-bound scope when applicable
Authentication Assurance
Audit
Least privilege
Read-only by default
```

---

# 37. Public Endpoints

Public institution data لا تستخدم Fake Membership أو Tenant bypass.

تستخدم:

```text id="jdky1k"
Dedicated Public Queries
```

ترجع فقط البيانات المسموح نشرها حسب Visibility/Verification policy.

---

# 38. Background Jobs

Background Job لا تنتحل `ICurrentActor`.

العملية تكون:

```text id="ilup7i"
Institution-scoped system operation
→ explicit InstitutionId

User-initiated async operation
→ InitiatorUserAccountId + InstitutionId

Platform operation
→ explicit platform authorization path
```

القواعد:

- لا نخزن `IsAuthorized = true` أو Permission snapshot ونعتبرها صالحة بلا نهاية.
- Job الحساسة تعيد فحص Account/relationship/permission/scope/assurance المطلوبة عند بدء التنفيذ.
- لو تغيرت الصلاحية قبل التنفيذ تفشل/تلغى Fail Closed وفق حالة Job الموثقة.
- بعد بدء Transaction حساسة، الـauthoritative state التي يمكن أن تتسابق يعاد فحصها داخل الـboundary المناسبة وفق T15/T16.
- نسجل Initiator منفصلًا عن System executor لأغراض Audit، ولا ننشئ Fake `ICurrentActor`.
- System-initiated jobs تحتاج Operation identity ثابتة وAllow-list صريحة، وليس global bypass.

---

# 39. Imports

Import Job لا تحصل على Authorization bypass.

تحمل:

```text id="i7mgtp"
InstitutionId
Initiator
Authorized operation
```

وتستخدم Contracts الـModules بدل تعديل Tables مباشرة.

صلاحية بدء Import لا تعني أن كل Row مسموح تنفيذها؛ كل command ناتجة تظل خاضعة لـtenant integrity وbusiness validation، والعمليات الحساسة تعاد مراجعة Authorization عند التنفيذ كما في Background Jobs.

---

# 40. Reporting & Exports

Institution report تراجع حسب الحاجة:

```text id="e93n25"
Institution
Permission
Scope
Sensitive-data requirements
```

Sensitive export قد تحتاج:

```text id="v2lzn4"
Permission
+
MFA
+
Recent Authentication
+
Audit
```

---

# 41. 401 / 403 / 404

## 401

```text id="5suo9x"
Authentication missing or invalid
```

## 403

```text id="hm5lbd"
Authenticated actor
+
known/visible authorization boundary
+
insufficient permission/assurance
```

## 404

يستخدم افتراضيًا عندما نحتاج إخفاء وجود Resource خارج Tenant/visibility boundary.

```text id="8sdtj3"
Resource not visible
→ 404
```

ولا نكشف:

```text id="9bpvq5"
record exists in another institution
```

شكل الـProblemDetails النهائي يتبع T32.

---

# 42. Transaction-sensitive Authorization

Authorization pre-check لا تحمي من Race وحدها.

لو State الأمنية أو Business state يمكن أن تتغير أثناء عملية حساسة:

```text id="shzn9p"
Re-check authoritative state
+
Concurrency protection
```

في الـboundary المناسبة.

الـBusiness state المملوكة لنفس Module يعاد فحصها داخل Transaction مع آلية T16. أما Membership/Permission في Module أخرى فلا نفتح لها Cross-module lock: نقطة السماح هي آخر Server-side authorization check قبل بدء الـmutation، وأي revoke بعدها يؤثر على العمليات اللاحقة لا يلغي Commit جارية بأثر رجعي. الـqueued/long-running work تعيد Authorization عند التنفيذ، والتفاصيل في T15.

---

# 43. No Authorization God Service

نقسم المسؤوليات:

```text id="mx183a"
IdentityAccess
→ authentication evidence

Institutions
→ staff membership / role / capability / branch scope

Students
→ student / guardian relationships

Business Module
→ resource-specific rules + business eligibility

AuditApprovals
→ approval / conflict workflow

PlatformAdministration
→ exceptional platform access

HTTP/T32
→ response mapping
```

ولا ننشئ Service واحدة تعرف كل ما سبق.

---

# 44. Domain Independence

الـDomain لا تعتمد على:

```text id="qbgje5"
HttpContext
ClaimsPrincipal
ASP.NET Authorization
Authorization Attributes
```

والـHandler لا تقرأ Claims مباشرة.

---

# 45. Tests

لازم نغطي على الأقل:

```text id="0o7som"
No active access relationship
Suspended membership
Role/capability changes
Branch scope violations

Cross-tenant resource access
IDOR

Student access with/without valid record
Guardian access by scope/assurance
Relationship ID belongs to another actor
Same actor with Staff + Student + Guardian relationships
No cross-access-kind permission union
No automatic fallback to a different AccessKind after deny

Missing MFA
Stale recent authentication
Wrong StepUpPurpose
Wrong StepUp target resource
Expired / consumed StepUpGrant
Single-use StepUp concurrent consumption
OperationAuthorizationTicket wrong session/module/operation/resource/OperationId
Concurrent ticket use with one business effect only
Authorization revoked before target mutation begins
Target failure then same-intent retry before ticket expiry

PrimaryOwner without bypass
Delegated representative restrictions

Self approval / related-party denial

Platform exceptional access
Permission exists but subscription entitlement missing
Permission revoked before queued job execution
System job without explicit operation identity

JWT without institution authorization claims
```

واختبارات Tenant/DB constraints تستخدم PostgreSQL حقيقية.

---

# القواعد النهائية

1. Authentication وAuthorization منفصلان.
2. `ICurrentActor` identity-only.
3. لا يوجد AccountMode أو StudentMode.
4. InstitutionId من Client Candidate فقط.
5. Institution Context تبنى Server-side.
6. كل Institution access لها Access Relationship حقيقية.
7. Staff تستخدم Membership + Role + Scope + Capabilities.
8. Student تستخدم StudentProfile/InstitutionStudentRecord + Eligibility.
9. Guardian تستخدم GuardianRelationship + Scope + Assurance + Eligibility.
10. Role/Capability لا تستبدل Resource authorization.
11. Permission لا تستبدل Business invariant.
12. Branch authorization صريحة.
13. JWT ليست Permission cache.
14. لا `[Authorize(Roles = ...)]` للأدوار المؤسسية.
15. MFA/Step-up ترفع Authentication Assurance فقط.
16. PrimaryOwner ليست bypass.
17. AuthorizedRepresentative ليست Owner تلقائيًا.
18. Conflict/Approval rules لا تتجاوزها Permission.
19. Platform access ليست All-Tenant bypass.
20. Authorization state تراجع Server-side.
21. Cross-tenant hidden resources غالبًا 404.
22. Business Modules لا تستخدم Foreign DbContexts.
23. Domain/Handlers لا تعتمد على HttpContext.
24. Background Jobs لا تنتحل CurrentActor.
25. Authorization tests إلزامية.
26. T14 هي الشكل المرجعي لـInstitution Context وT09 تعتمد عليها.
27. كل قرار Authorization يستخدم Access path واحدة typed ومتحققًا منها.
28. ممنوع جمع Permissions عبر Staff/Student/Guardian relationships.
29. كل Relationship مرتبطة بالـActor/Person الحالية؛ ID وحدها لا تكفي.
30. Permission Catalog/Role Matrix ثابتة وVersion-controlled لكل Module منفذة.
31. GuardianRelationshipAssurance منفصلة عن AuthenticationAssurance.
32. MFA إلزامية للأدوار والصلاحيات الحساسة المحددة.
33. العمليات الحساسة تستخدم StepUpGrant مطابقة للPurpose/Target وليست Session elevation عامة.
34. Subscription entitlement وPermission شرطان منفصلان.
35. User-initiated sensitive jobs تعيد Authorization وقت التنفيذ.
36. System jobs تستخدم Operation identity وAllow-list صريحة.
37. High-risk Step-up تنتقل للـTarget Module عبر `OperationAuthorizationTicket` مقيدة بالـOperationId، وليست Cross-module transaction.
38. Target Module تعيد Authorization الحالية ثم تثبت `TicketId + OperationId` مع الـbusiness mutation ذريًا.

---

# خارج نطاق T14

```text id="70vkd0"
Final Institution transport / routes
→ T31

ProblemDetails / exact HTTP error contract
→ T32

Cross-module contract direction
especially Student/Guardian context resolution
→ T18

WebAuthn / Passkeys
Trusted Devices
Additional MFA methods
→ later Identity/Security features; Core V1 TOTP/Recovery/Step-up في T13

Approval workflow implementation
Audit implementation
→ AuditApprovals / T27

Custom roles / dynamic permission authoring
→ deferred

Platform impersonation / break-glass implementation
→ PlatformAdministration + Security decisions
```

---

# القرار النهائي المختصر

> EduCenterOS تستخدم Authorization Server-side مبنية على **Access Relationship حقيقية**، وليست على Account Type أو Claims قديمة داخل JWT.

> بالنسبة للStaff، الصلاحية تعتمد على `InstitutionMembership + Fixed Role + Branch Scope + Optional Capabilities`. أما Student وGuardian فلهم Access Models مستقلة تعتمد على العلاقات والسجلات والـEligibility الخاصة بهم، بدون `StudentMode` أو Staff Membership وهمية.

> حتى لو امتلك الشخص أكثر من علاقة داخل المؤسسة نفسها، كل Authorization decision تستخدم Access path واحدة typed ومثبتة الارتباط بالـActor؛ لا يحدث permission union أو fallback تلقائي بين Staff وStudent وGuardian.

> Authorization الكاملة قد تجمع Permission وScope وResource relationship وEligibility وAuthentication Assurance وConflict/Approval rules. وجود Permission وحدها لا يعني السماح بالعملية.

> MFA وRecent Authentication وStep-up هي إثباتات Authentication إضافية ولا تمنح صلاحيات جديدة. PrimaryOwner وPlatform Admin لا تحصلان على bypass عام.

> الأدوار والصلاحيات الحساسة تتطلب TOTP MFA في Core V1، والعمليات الأعلى خطورة تستهلك `StepUpGrant` قصيرة العمر ثم تستخدم `OperationAuthorizationTicket` أقصر عمرًا ومقيدة بالـSession والـModule والعملية والـTarget و`OperationId`.

> كل Module تظل مالكة لقواعد Resources الخاصة بها، لذلك لا يوجد Authorization God Service ولا Direct cross-module DbContext access.
