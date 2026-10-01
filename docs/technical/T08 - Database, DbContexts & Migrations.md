# T08 - Database, DbContexts & Migrations

## القرار

EduCenterOS يستخدم:

```text
One PostgreSQL Database
+
Schema per active Business Module
+
DbContext per Module
+
Migrations per Module
+
Migration History per Module
```

مع الحفاظ على ملكية واضحة للبيانات.

---

# 1. Database واحدة

في Core V1 نستخدم:

```text
Database: educenteros
```

ولا نستخدم:

```text
Database per Module
Database per Institution
Schema per Institution
```

الهدف:

- تشغيل أبسط.
- Backup/Restore أبسط.
- Transactions محلية عند الحاجة.
- تكلفة أقل.
- مناسب للـModular Monolith.

---

# 2. Schema لكل Module فعالة

الأسماء الأساسية:

```text
identity_access
institutions
subscriptions
academic
students
enrollments
sessions_attendance
student_finance
branch_finance
teacher_compensation
notifications
audit_approvals
reporting
imports
platform_administration
```

لا ننشئ Schema قبل بدء أول Feature تحتاج Persistence داخل الـModule.

وبالتالي لا ننشئ حاليًا:

```text
marketplace
marketplace_finance
```

لأنهم Phase 2.

---

# 3. DbContext لكل Module

كل Module تملك DbContext خاصة بها.

مثال:

```text
AcademicDbContext
StudentsDbContext
EnrollmentsDbContext
StudentFinanceDbContext
```

القواعد:

1. DbContext تعرف Entities الخاصة بنفس Module فقط.
2. لا تحتوي `DbSet` من Module أخرى.
3. لا يتم حقن DbContext خاصة بـModule أخرى داخل Handler.
4. DbContext تكون `internal`.
5. Configurations وMigrations داخل Infrastructure الخاصة بالموديول.

ممنوع إنشاء:

```text
EduCenterOSDbContext
ApplicationDbContext
```

تحتوي كل النظام.

---

# 4. ملكية البيانات

الـModule المالكة للجدول هي الوحيدة التي:

- تنشئه.
- تغير Schema الخاصة به.
- تكتب عليه.
- تفسر حالاته.
- تحمي Business Rules الخاصة به.
- تصدر Migrations الخاصة به.

ممنوع:

```text
Direct writes to another module
Using another module DbContext
Raw SQL to modify another module
Mapping another module entities
```

---

# 5. العلاقات داخل نفس Module

داخل Module واحدة نستخدم العلاقات الطبيعية حسب الـDomain:

```text
Foreign Keys
Navigation Properties
Unique Constraints
Check Constraints
Composite Indexes
```

لكن وجود علاقة في Database لا يعني بالضرورة أنها جزء من نفس Aggregate.

---

# 6. Cross-Module References

القاعدة الافتراضية:

> **Cross-Module Foreign Keys ممنوعة افتراضيًا، وليست ممنوعة مطلقًا.**

الـBusiness Modules تتعامل مع بعضها من خلال:

```text
IDs
Contracts
Application orchestration
Events
```

ولا نستخدم Navigation Properties بين Modules.

---

# 7. Structural Cross-Module Foreign Keys

يسمح باستثناء محدود عندما تكون العلاقة:

1. Structural وليست Business workflow متغيرة.
2. الـPrincipal record مستقرة.
3. الـFK تمنع Corruption مهمة.
4. الفائدة في Referential Integrity أعلى من تكلفة الـCoupling.
5. تم توثيق الاعتماد صراحة.

أهم مثال:

```text
InstitutionId
BranchId
```

في Record تابعة لفرع.

يمكن استخدام Constraint مثل:

```text
(institution_id, branch_id)
→
institutions.branches (institution_id, id)
```

لمنع حالة مثل:

```text
Record.InstitutionId = Institution A
Record.BranchId      = Branch of Institution B
```

---

# 8. قواعد Cross-Module FK

حتى عند السماح بها:

- لا توجد EF Navigation Property عبر Modules.
- DbContext لا تقوم Mapping للـEntity الأجنبية.
- لا يستخدم `Include()` عبر حدود Module.
- لا يسمح Cascade Delete عبر Modules افتراضيًا.
- الـDependent Module هي المسؤولة عن Constraint الخاصة بجدولها.
- Migration dependency يجب أن تكون معروفة.
- Migration الخاصة بالـPrincipal يجب أن تعمل أولًا.
- أي FK جديدة تحتاج مراجعة Architecture واضحة.

يعني:

```text
Database Referential Integrity
≠
Module ownership
```

وجود FK لا يعطي Module حق قراءة أو تعديل الـModule الأخرى مباشرة.

---

# 9. Cross-Module Business Relationships

العلاقات المتغيرة أو التي تمثل Workflow لا ننشئ لها FK تلقائيًا.

مثل:

```text
TeacherSettlement → Session
Payment → Enrollment
Notification → Business Entity
Approval → Target Operation
```

تظل غالبًا References باستخدام IDs وContracts.

القرار يؤخذ حسب طبيعة العلاقة وليس لأن الاثنين موجودان في نفس Database.

## 9.1 Same-Database Transaction Exception

استثناء `RecordCashPayment` المعتمد في T15 لا يغير Data ownership:

```text
StudentFinanceDbContext
→ تكتب Payment وجداول StudentFinance فقط

BranchFinanceDbContext
→ تكتب CashMovement وجداول BranchFinance فقط
```

الـFeature-specific orchestrator تفتح Connection/`DbTransaction` واحدة وتطلب من كل Module-owned participant أن تعمل Context الخاصة بها enlist في نفس transaction. لا تحقن Module DbContext داخل الأخرى، ولا تضيف Foreign Entities أو Navigation Properties، ولا تكتب Raw SQL في schema أجنبية.

ده استثناء تنفيذي مسمى لنفس PostgreSQL Database، وليس `Global UnitOfWork` أو صلاحية عامة لمشاركة Contexts. أي استثناء آخر يحتاج قرارًا معماريًا جديدًا في T15.

---

# 10. Cross-Module Reads

Business Module لا تعمل Direct Join على جداول Module أخرى كطريقة عادية لتنفيذ Business Logic.

مثلًا `Academic` لا تعمل:

```sql
JOIN enrollments...
```

لحساب Business Rule من وراء Module المالكة.

نستخدم:

```text
Contract Query
Application Composition
Read Model
Snapshot
```

حسب الحاجة.

---

# 11. Reporting Exception

`Reporting` مسموح لها بعمل **Read-only cross-schema composition**.

يمكن أن تستخدم:

```text
SQL projections
Views
Materialized Views
Reporting Read Models
```

عبر عدة Modules.

لكن:

```text
Reporting = Read Only
Reporting ≠ Source of Truth
Reporting ≠ Business Writer
```

ولا تستخدم Views كطريق خلفي لتعديل بيانات Module أخرى.

---

# 12. Snapshots

يمكن لـModule الاحتفاظ بنسخة محدودة من بيانات Module أخرى لأغراض تاريخية.

مثل:

```text
StudentNameSnapshot
TeacherNameSnapshot
InstitutionNameSnapshot
```

عند إنشاء مستند أو Settlement تاريخية.

الـSnapshot:

- ليست Source of Truth.
- لا تحتوي Entity كاملة.
- لا تستخدم بدل Contract عندما نحتاج القيمة الحالية.

---

# 13. Migrations

كل DbContext لها Migrations مستقلة داخل Module الخاصة بها.

مثال:

```text
Academic/
└── Infrastructure/
    └── Database/
        └── Migrations/
```

القواعد:

- Migration تغير Schema الخاصة بالموديول فقط، باستثناء Constraint cross-module معتمدة على جدولها.
- لا توجد Business Migrations داخل `Api`.
- لا نعدل Migration تم تطبيقها في بيئة مشتركة.
- أي تصحيح يتم Migration جديدة.
- يتم تحديد DbContext صراحة عند إنشاء Migration.

---

# 14. Migration History

كل Module لديها:

```text
<schema>.__ef_migrations_history
```

مثل:

```text
academic.__ef_migrations_history
students.__ef_migrations_history
student_finance.__ef_migrations_history
```

ولا نشارك Migration History واحدة بين جميع الـDbContexts.

---

# 15. Migration Dependencies

لو Migration تعتمد على Object تملكه Module أخرى، مثل Structural FK:

```text
Institutions migration
↓
Academic migration containing Branch FK
```

يجب أن يكون ترتيب التطبيق واضحًا.

ولا ننشئ Dependency دائرية بين Migrations.

---

# 16. Production Migrations

في Production:

> الـAPI لا تشغل Migrations تلقائيًا عند Startup.

تطبق الـMigrations من خلال Deployment/Migration step واضحة.

السبب:

- التحكم في ترتيب الـModules.
- مراجعة Migration قبل التطبيق.
- التعامل مع الأخطاء بوضوح.
- منع أكثر من instance من محاولة Migration في نفس الوقت.

---

# 17. Naming

Database objects تستخدم:

```text
lowercase
snake_case
```

مثل:

```text
study_groups
institution_id
created_at
```

Business tables لا توضع داخل `public` Schema.

---

# 18. Raw SQL

EF Core هو Default.

Raw SQL مسموح عند وجود سبب واضح مثل:

```text
Heavy reporting
Bulk operation
PostgreSQL-specific operation
Migration data transformation
```

لكن لا تستخدم لتجاوز Module ownership.

---

# 19. Architecture & Integration Tests

الاختبارات يجب أن تتحقق من:

- كل Entity في Schema الصحيحة.
- كل DbContext تملك Entities الخاصة بها فقط.
- عدم حقن DbContext Module داخل Module أخرى.
- عدم وجود Cross-Module Navigation Properties.
- عدم وجود Direct Business Writes عبر Schemas.
- Migration history مستقلة.
- Migrations تعمل من Database فارغة.
- Structural FKs المعتمدة صحيحة.
- عدم وجود Cross-Module Cascade Deletes غير معتمدة.
- Reporting cross-schema access تظل Read-only.

---

# القواعد النهائية

1. Database واحدة في Core V1.
2. Schema مستقلة لكل Module فعالة.
3. DbContext مستقلة لكل Module.
4. Migrations وMigration History مستقلة.
5. Module تكتب على جداولها فقط.
6. ممنوع استخدام DbContext Module أخرى.
7. Cross-Module Navigation Properties ممنوعة.
8. Cross-Module FK ممنوعة افتراضيًا.
9. Structural FK يمكن السماح بها استثنائيًا لحماية Integrity مهمة.
10. لا Cascade Delete عبر Modules افتراضيًا.
11. Reporting مسموح لها Cross-Schema Reads فقط.
12. Production Migrations لا تعمل تلقائيًا مع Startup.
13. لا ننشئ Schemas أو DbContexts قبل الحاجة الفعلية.
14. Marketplace Schemas لا تنشأ قبل Phase 2.
15. `RecordCashPayment` فقط يمكنها مشاركة PostgreSQL transaction حسب T15 مع بقاء كل Context مالكة لكتابتها.

---

# خارج نطاق T08

يتحدد في قرارات أخرى:

```text
Multi-Tenancy
Tenant Query Filters
Row-Level Security

Transactions
Concurrency
Locking

Module communication
Events
Outbox

Database production roles
Backup / Restore
RPO / RTO
```

---

# القرار النهائي المختصر

> EduCenterOS يستخدم **PostgreSQL Database واحدة، مع Schema وDbContext وMigrations مستقلة لكل Business Module فعالة**.

> كل Module تملك الكتابة على جداولها فقط، ولا تستخدم DbContext أو Domain Entities الخاصة بـModule أخرى.

> مشاركة `DbTransaction` في استثناء `RecordCashPayment` لا تعني مشاركة DbContext أو ownership؛ كل participant تكتب من خلال Context الموديول المالكة فقط.

> Cross-Module Foreign Keys **ممنوعة افتراضيًا وليست ممنوعة مطلقًا**؛ يسمح فقط بعلاقات Structural مدروسة مثل حماية توافق `InstitutionId + BranchId`، مع استمرار منع Navigation Properties وDirect cross-module writes.

> Reporting هي الاستثناء المقصود للـCross-Schema Reads، وتظل Read-only وليست Source of Truth.
