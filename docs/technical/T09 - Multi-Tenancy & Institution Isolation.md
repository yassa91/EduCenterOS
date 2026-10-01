# T09 - Multi-Tenancy & Institution Isolation

## القرار

EduCenterOS يستخدم:

```text id="z15eyh"
Shared PostgreSQL Database
+
Shared Tables per Business Module
+
InstitutionId as Tenant Discriminator
+
BranchId when branch-scoped
+
Validated Institution Context
+
Global Query Filters
+
Write Guards
+
Database Constraints
+
Fail Closed
```

الهدف:

> مستخدم أو Process لا يقرأ أو يعدل بيانات مؤسسة إلا بعد إثبات حقه في الوصول إليها.

---

# 1. نموذج الـMulti-Tenancy

كل البيانات التابعة لمؤسسة تحمل:

```csharp id="x5ag02"
Guid InstitutionId
```

ولو السجل تابع لفرع:

```csharp id="yv7ckw"
Guid InstitutionId
Guid BranchId
```

ولا نستخدم:

```text id="6ibme1"
Database per Institution
Schema per Institution
Table per Institution
```

---

# 2. Schema vs Tenant

حسب T08:

```text id="26d1sf"
Schema
→ تحدد الـModule المالكة للبيانات

InstitutionId
→ تحدد المؤسسة المالكة للصف
```

مثال:

```text id="5g3pkr"
academic.study_groups

Schema:
academic

Tenant:
institution_id
```

---

# 3. مش كل البيانات Tenant-scoped

قبل إضافة `InstitutionId` لأي Entity، نصنفها.

## Platform / Global Data

مثل:

```text id="ea0ik0"
UserAccount
PersonIdentity

EducationSystem
Stage
Grade
ReferenceSubject

SubscriptionPlan
PlatformConfiguration
```

لا نضيف لها `InstitutionId` بشكل مصطنع.

---

## Institution-scoped Data

مثل:

```text id="xmscfq"
Branch
InstitutionMembership
AcademicYear
StudyGroup
InstitutionStudentRecord
StudentEnrollment
Charge
Payment
TeacherContract
Expense
Safe
```

تحمل `InstitutionId`.

---

## Institution Root

```text id="rx42si"
Institution.Id
=
Tenant Identifier
```

ولا تحتاج `InstitutionId` إضافية داخل نفس Record.

---

# 4. Requested Institution ليست موثوقة

أي `InstitutionId` تأتي من:

```text id="2f2n1s"
Route
Header
Query String
Request Body
Cookie
Client State
```

تعتبر فقط:

```text id="1e25ro"
Requested Institution Candidate
```

وليست Authorization.

---

# 5. بناء Institution Context

السياق الموثوق يبنى Server-side من:

```text id="zvhvsw"
Authenticated Actor
+
Account Status
+
Requested Institution
+
Active Access Relationship
+
Role / Scope / Capabilities when applicable
+
Resource Eligibility
+
Conflict Rules
+
Approval Rules when applicable
+
Authentication Assurance
```

ولا يوجد:

```text id="smfpia"
AccountMode
StudentMode
GeneralMode
```

ضمن القرار.

---

# 6. Institution Context

نستخدم Context واحدة لكل Request أو Operation بعد نجاح التحقق.

T14 هي التعريف المرجعي النهائي للـInstitution Context. الشكل المشترك يظل صغيرًا:

```csharp id="4mxng0"
IInstitutionContext
{
    InstitutionId
    UserAccountId
    AccessKind
}
```

أما Relationship references فتكون في Context typed حسب مسار الوصول:

```text id="pzpzcu"
StaffInstitutionContext
→ InstitutionMembershipId

StudentInstitutionContext
→ StudentProfileId + InstitutionStudentRecordId

GuardianInstitutionContext
→ GuardianRelationshipId + InstitutionStudentRecordId

PlatformSupportInstitutionContext
→ PlatformCaseId + ExceptionalAccessGrantId
```

`Role` و`BranchScope` و`Capabilities` تقرأ من مصدرها Server-side عند فحص العملية، وAuthentication evidence تأتي من `IAuthenticationContext`. لا نحول الـContext العامة إلى stale authorization snapshot.

كل Authorization decision تستخدم Access path واحدة فقط؛ لا نجمع Staff/Student/Guardian permissions داخل bag واحدة.

القواعد:

- Immutable بعد إنشائها.
- لا تغير Institution أثناء نفس Scope.
- لا تخزن Static.
- لا تستخدم `Guid.Empty`.
- لا يستخدم `null` بمعنى All Institutions.
- غيابها في عملية Institution-scoped يؤدي إلى Fail-Closed.
- كل typed relationship يجب إثبات ارتباطها بالـActor الحالي؛ معرفة ID لا تكفي.

---

# 7. أنواع الوصول

مش كل وصول للمؤسسة يأتي من `InstitutionMembership`.

## Staff / Institutional Roles

يعتمد على:

```text id="b9wptq"
Active InstitutionMembership
+
Role
+
Scope
+
Capabilities
+
Account Status
+
Authentication Assurance
+
Conflict / Approval rules when applicable
```

---

## Student Access

لا يحتاج Staff Membership.

يعتمد على:

```text id="lrrfjz"
Authenticated UserAccount
+
Linked StudentProfile
+
InstitutionStudentRecord
+
Eligibility for requested resource/action
```

وجود `StudentProfile` وحدها لا يمنح وصولًا لكل مؤسسة.

---

## Guardian Access

يعتمد على:

```text id="x2xyiu"
GuardianRelationship
+
Relationship Scope
+
Assurance Level
+
InstitutionStudentRecord
+
Requested Operation
+
Eligibility
```

العلاقة Institution-scoped لا تمنح وصولًا لمؤسسة أخرى.

والعلاقة Platform-level لا تعني الوصول التلقائي لكل بيانات الطالب؛ ما زالت Resource Authorization مطلوبة.

---

## Public Access

الزائر العام لا يستخدم Tenant Context داخلية.

يستخدم Public Queries مخصصة تعيد فقط البيانات المسموح نشرها حسب:

```text id="5zecv5"
Hidden
DirectLinkOnly
PublicSearch
```

---

## Platform Administration

مسؤول المنصة لا يملك:

```text id="1pgl41"
AllTenantsContext
```

دائمة.

أي وصول استثنائي لبيانات مؤسسة يحتاج Use Case محددة ومخولة ومراجعة، مع:

```text id="rjqzq7"
Target Institution
Reason
Case / Reference
Authentication Assurance
Audit
Minimum required scope
```

---

# 8. JWT ليست Source of Truth للصلاحيات

يمكن أن تحتوي Token على معلومات Authentication مستقرة نسبيًا مثل:

```text id="257zgk"
UserId
SessionId
Authentication Time
Security Version
```

لكن لا نعتمد كحقيقة نهائية على Claims قابلة للتغيير مثل:

```text id="ea8yx8"
Membership status
Role
Capabilities
Branch scope
Institution status
```

لأنها قد تصبح Stale.

الـBackend يعيد تقييم Access Relationship المطلوبة.

---

# 9. Global Query Filters

نستخدم EF Core Query Filters لعزل القراءة افتراضيًا.

مثال:

```text id="37utnm"
WHERE institution_id = currentInstitutionId
```

لكن:

> Query Filter ليست Authorization System كاملة.

هي فقط طبقة دفاع ضمن عدة طبقات.

---

# 10. Branch Authorization

لا نعتمد على Global Branch Filter عام لكل البيانات.

السبب:

بعض المستخدمين:

```text id="68p6k7"
Institution-wide
```

وبعض العمليات تحتاج أكثر من Branch.

لذلك:

```text id="wt7ddh"
Institution Isolation
→ Global baseline

Branch Access
→ Explicit authorization according to resource/action
```

---

# 11. Write Isolation

عند الكتابة يجب التحقق من:

```text id="y9wkzw"
Entity.InstitutionId
==
Validated InstitutionId
```

وعند التعديل:

```text id="jy1ey4"
InstitutionId cannot change
```

ويمنع الاعتماد على:

```text id="t15yd0"
request.InstitutionId
```

كقيمة موثوقة لإنشاء Entity.

---

# 12. Branch Integrity

في السجلات التابعة لفرع:

```text id="unb5mr"
InstitutionId + BranchId
```

لازم يكونوا متوافقين.

وحسب T08 يمكن استخدام Structural Composite Constraint / FK معتمدة مثل:

```text id="v04vni"
(institution_id, branch_id)
→
institutions.branches
```

لمنع ربط Record بفرع من مؤسسة أخرى.

---

# 13. `IgnoreQueryFilters`

ممنوعة في Business Code العادية.

مسموحة فقط داخل Use Cases محددة مثل:

```text id="929krh"
Platform investigation
Reconciliation
Migration / Maintenance
Approved support workflow
Test infrastructure
```

مع Authorization وAudit واضحين.

ولا يوجد Helper عام مثل:

```text id="3tfn0l"
DisableTenantIsolation()
```

---

# 14. Cross-Tenant Resource Lookup

لو المستخدم يطلب ID تابع لمؤسسة أخرى:

لا نكشف عادة:

> السجل موجود لكن تابع لمؤسسة ثانية.

يتم التعامل غالبًا معه كـ:

```text id="kpx8xk"
Not Found
```

داخل السياق الحالي.

وده مهم ضد IDOR.

---

# 15. Background Jobs

أي Job Institution-scoped تحمل:

```text id="7n0dkr"
InstitutionId
```

بصورة صريحة.

وتنشئ Scope جديدة عند التنفيذ.

ممنوع الاعتماد على:

```text id="ip47fi"
Ambient HTTP tenant context
Static tenant
Last active tenant
```

وعند معالجة أكثر من مؤسسة:

```text id="85k5gz"
One explicit tenant scope at a time
```

---

# 16. Events

أي Event تخص مؤسسة تحتوي:

```text id="cltp63"
InstitutionId
```

والـConsumer يعيد إنشاء Scope صريحة.

Retry تظل مرتبطة بنفس Tenant.

ولا يتم تمرير Domain Entity كاملة داخل Event.

---

# 17. Cache

أي Cache لبيانات مؤسسية يجب أن تحتوي Tenant في Key.

مثل:

```text id="b7vbpf"
institution:{institutionId}:group:{groupId}
```

ولا تستخدم Cache كـAuthorization Source.

---

# 18. Files & Exports

أي File Institution-scoped تحتوي Metadata تحدد:

```text id="n8bayd"
InstitutionId
```

والـDownload / Signed URL / Export يتم بعد Authorization.

اسم الملف أو Storage Path وحدهما لا يمثلان حماية.

---

# 19. Reporting

Reporting Read Models المؤسسية تحتفظ بـ:

```text id="w4vwo2"
InstitutionId
```

وتلتزم بالعزل.

أما Platform Reporting الشاملة للمؤسسات فهي Use Cases منفصلة ومخولة.

Reporting لا تستخدم لتجاوز Business Module ownership.

---

# 20. Imports

كل Import Job مؤسسية مرتبطة بـ:

```text id="u036ba"
InstitutionId
```

من بداية:

```text id="h7y70f"
Upload
→ Preview
→ Validation
→ Commit
```

ولا يستطيع Import نقل Row لمؤسسة أخرى.

---

# 21. DbContext Lifetime

الاختيار الافتراضي:

```text id="yf6lfk"
Scoped DbContext
per Request / Operation
```

ولا نستخدم:

```text id="dfpiyn"
Singleton DbContext
Static DbContext
Shared DbContext between threads
```

---

# 22. DbContext Pooling

لا نستخدم:

```text id="n3e3mu"
AddDbContextPool
```

في البداية.

السبب:

> Tenant state security أهم من Optimization غير مثبتة.

يعاد تقييم Pooling فقط بعد قياس فعلي واختبارات متخصصة تمنع Tenant state leakage.

---

# 23. PostgreSQL Row-Level Security

لا نفعّل RLS في Core V1.

نبدأ بـ:

```text id="wzkbvr"
Validated Context
+
Authorization
+
Query Filters
+
Write Guards
+
Database Constraints
+
Tests
```

لكن تصميم الجداول يظل `RLS-ready`.

إضافة RLS مستقبلًا تحتاج قرارًا تقنيًا مستقلًا.

---

# 24. Fail Closed

أي Institution-scoped operation بدون Context صحيحة:

```text id="364aax"
FAIL
```

ولا نحاول:

```text id="a4jzx3"
Use first institution
Use last workspace
Guess from entity ID
Use null as all tenants
Continue partially
```

---

# 25. Tenant Isolation Tests

Integration/Security Tests لازم تغطي على الأقل:

```text id="x4oxrj"
Cross-tenant read
Cross-tenant write
Cross-tenant delete/archive

Branch from another institution
Branch outside actor scope

IDOR
Route/header tampering
Overposting

Stale permissions
Suspended membership

Background jobs
Cache
Files
Exports
Reporting
Imports

Parallel requests for different tenants
Same user in multiple institutions

Student access
Guardian access
Platform exceptional access
```

Tenant Isolation تعتبر Security Requirement أساسية وليست مجرد Query convention.

---

# القواعد النهائية

1. `InstitutionId` إلزامية لكل Institution-scoped Entity.
2. `BranchId` لا تستبدل `InstitutionId`.
3. Institution القادمة من Client غير موثوقة.
4. الـBackend يبني Validated Institution Context.
5. لا يوجد AccountMode ضمن Authorization Model.
6. Staff وStudent وGuardian لهم Access Relationships مختلفة.
7. JWT claims المتغيرة ليست Source of Truth.
8. Query Filters تعزل القراءة افتراضيًا.
9. Query Filters ليست طبقة الحماية الوحيدة.
10. Writes تتحقق من Tenant Context.
11. `InstitutionId` immutable بعد الإنشاء.
12. Branch authorization صريحة.
13. Structural constraints تحمي Tenant integrity عند الحاجة.
14. `IgnoreQueryFilters` ليست متاحة للـBusiness Code الطبيعية.
15. Background Jobs وEvents وCache وFiles تحمل Tenant Context صريحة.
16. Platform Admin لا تملك All-Tenant bypass دائم.
17. DbContext Pooling غير مستخدمة في البداية.
18. PostgreSQL RLS غير مفعلة في Core V1.
19. غياب Context يؤدي إلى Fail-Closed.
20. Tenant Isolation تتغطى باختبارات إلزامية.

---

# خارج نطاق T09

يتحدد في قرارات منفصلة:

```text id="b03n5q"
Final URL / workspace convention
Authentication implementation
Authorization engine implementation
BranchScope implementation

Cache technology
Background job library

Event dispatch / Outbox
Audit implementation

PostgreSQL RLS future design
Database roles

Encryption
Retention
Backup / DR
```

---

# القرار النهائي المختصر

> EduCenterOS يستخدم **Shared-table Multi-Tenancy بواسطة `InstitutionId`** داخل قاعدة PostgreSQL المشتركة.

> المؤسسة المطلوبة من الـClient ليست موثوقة؛ الـBackend ينشئ `Validated Institution Context` بعد التحقق من **Active Access Relationship، Role/Scope/Capabilities عند الحاجة، Eligibility، Conflict Rules، Approval Rules وAuthentication Assurance**.

> Student وGuardian access لا تعتمد على Staff Membership أو Account Modes، وكل نوع وصول له علاقته وقواعده الخاصة.

> Query Filters تعزل القراءة، Write Guards وConstraints تحمي الكتابة، وأي عملية Institution-scoped بدون Context صحيحة **تفشل افتراضيًا**.
