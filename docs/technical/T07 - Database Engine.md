# T07 - Database Engine

> **2026-10-03 — Current development/testing override:** [T41 — Supabase Development & Testing](T41%20-%20Supabase%20Development%20%26%20Testing.md) replaces local PostgreSQL/Docker/Testcontainers provisioning and the synthetic-only infrastructure credential rule. The owner requested two cloud projects and an empty development database. The following earlier decision/evidence remains historical where it conflicts with T41. Remote compatibility verification is required before DB01 is complete.

## القرار

EduCenterOS يعتمد على:

```text id="of03dn"
PostgreSQL 18
+
Entity Framework Core 10
+
Npgsql.EntityFrameworkCore.PostgreSQL 10.x
```

كـDatabase Stack الأساسي.

---

# 1. Version Policy

نعتمد:

```text id="ux55hk"
PostgreSQL Major Version = 18
EF Core Major Version = 10
Npgsql Major Version = 10
```

ونستخدم أحدث Stable Patch معتمدة داخل نفس الـMajor Version.

ممنوع استخدام:

```text id="bw0xlr"
Preview
Beta
RC
```

داخل الـMain Solution.

أي Major Upgrade يحتاج:

- Compatibility review.
- Migration plan.
- Integration tests.
- قرار تقني موثق.

---

# 2. EF Core

EF Core هي أداة الـPersistence الأساسية.

تستخدم في:

```text id="512rcw"
Queries
Commands
Transactions
Migrations
Mappings
Constraints
Indexes
Concurrency
```

ولا نضيف Provider أو ORM إضافية بدون احتياج حقيقي.

Raw SQL أو أدوات أخرى مسموحة فقط لحالة محددة ومقاسة مثل:

```text id="4igfm8"
Heavy reporting query
Large bulk operation
Demonstrated performance problem
```

---

# 3. PostgreSQL-specific Features

مسموح استخدام خصائص PostgreSQL لما تضيف قيمة واضحة، مثل:

```text id="mi8l24"
Schemas
Partial Indexes
Expression Indexes
Generated Columns
jsonb
pg_trgm
Database Constraints
Locking features
```

لكن:

> لا نستخدم Feature خاصة بالمحرك بدون سبب واضح.

وفي نفس الوقت:

> لا نبني Database abstraction وهمية فقط لاحتمال تغيير PostgreSQL مستقبلًا.

---

# 4. أنواع البيانات

الاختيارات الأساسية:

```text id="il2wk6"
Guid            → uuid
string          → text / varchar
DateTimeOffset  → timestamptz
DateOnly        → date
TimeOnly        → time
bool            → boolean
int             → integer
long            → bigint
decimal         → numeric
byte[]          → bytea
```

---

# 5. الأموال

البيانات المالية تستخدم:

```text id="wdghfb"
C#
decimal

PostgreSQL
numeric(precision, scale)
```

ممنوع استخدام:

```text id="xy80pq"
float
double
real
```

للأموال.

كل Amount مهمة لازم تحدد لها:

```text id="sdkms5"
Precision
Scale
```

بصورة صريحة.

---

# 6. JSON

`jsonb` مسموحة في حالات مناسبة فقط.

لكن ممنوع استخدامها كبديل عن تصميم علائقي واضح للـBusiness Data الأساسية.

يعني لا نعمل:

```text id="ta6ptg"
Student
Payment
Enrollment
```

كلهم داخل JSON document كبيرة لمجرد سهولة البداية.

---

# 7. الوقت والتواريخ

القاعدة:

```text id="u9hx05"
Instant in time
→ UTC
→ DateTimeOffset / timestamptz
```

أما المفاهيم التي تمثل تاريخًا فقط مثل:

```text id="046cd6"
Birth Date
Academic Date
Due Date when no exact instant is intended
```

فتستخدم `DateOnly / date` حسب معنى الـDomain.

ويمنع استخدام:

```csharp id="n0fto3"
DateTime.Now
```

كمصدر Business Time.

يستخدم `IClock`.

---

# 8. Time Zones

لو Business Operation تعتمد على Local Time:

```text id="c64j90"
UTC instant
+
Explicit Time Zone Context
```

ولا نعتمد على Time Zone الخاصة بسيرفر الـAPI.

التفاصيل الدقيقة لإدارة Time Zones يمكن حسمها في قرار مستقل.

---

# 9. النصوص العربية

PostgreSQL تدعم Unicode، لكن Database Collation وحدها لا تحل كل مشاكل البحث العربي.

لو احتجنا Search/Matching أكثر تقدمًا يمكن استخدام:

```text id="i1c48e"
Application normalization
Normalized columns
Special indexes
pg_trgm
```

حسب الـFeature.

ولا نفرض Arabic normalization عامة بدون Use Case محددة.

---

# 10. Development & Integration Testing

التطوير والـIntegration Tests تستخدم PostgreSQL حقيقية أو Container.

```text id="eh7zfb"
Application
+
PostgreSQL 18 Container
```

ممنوع اعتبار:

```text id="1auphd"
EF Core InMemory Provider
```

بديلًا لاختبارات PostgreSQL الحقيقية.

Integration Tests يجب أن تغطي حسب الحاجة:

```text id="66hva3"
Migrations
Constraints
Transactions
Concurrency
Indexes behavior
Provider-specific behavior
```

---

# 11. Connection Secrets

Connection Strings وDatabase credentials تعتبر Secrets.

ممنوع وضع Production credentials داخل Git.

يستخدم:

```text id="8j7jb9"
Local secret mechanism for development
+
Production secret store
```

ولا تكتب Connection String كاملة داخل Logs.

---

# 12. Database Permissions

حساب التطبيق في Production يحصل على أقل Permissions يحتاجها.

ويمكن فصل:

```text id="h3mutk"
Application runtime account
```

عن:

```text id="yf88kj"
Migration account
```

لو بيئة التشغيل تحتاج ذلك.

---

# 13. Production Data

ممنوع استخدام Production Database للتطوير أو الاختبار.

وممنوع نسخ بيانات Production الحساسة إلى أجهزة المطورين كإجراء طبيعي.

---

# 14. خارج نطاق T07

T07 لا يحسم:

```text id="gtwrkv"
Schemas per Module
DbContexts
Migration layout
Cross-module foreign keys

Multi-Tenancy
Global Query Filters

Transaction boundaries
Concurrency strategy

Outbox / Events

Database naming conventions
Enum storage

Backups
RPO / RTO
High Availability
Read Replicas

Hosting Provider
```

كلها لها قرارات مستقلة.

---

# القواعد النهائية

1. PostgreSQL 18 هي قاعدة البيانات الأساسية.
2. EF Core 10 هي Persistence tool الافتراضية.
3. Npgsql 10.x هو Provider المعتمد.
4. Stable Versions فقط.
5. لا Provider ثانية بدون قرار مستقل.
6. الأموال تستخدم `decimal / numeric`.
7. الوقت الفعلي يخزن UTC.
8. `jsonb` ليست بديلًا للتصميم العلائقي.
9. PostgreSQL-specific features مسموحة عند وجود قيمة.
10. Integration Tests تستخدم PostgreSQL حقيقية.
11. Production secrets لا تدخل Git.
12. Production data لا تستخدم كTest data.
13. Database constraints خط دفاع مهم لكنها لا تستبدل Domain Rules أو Authorization.

---

# القرار النهائي المختصر

> EduCenterOS يعتمد على **PostgreSQL 18 + EF Core 10 + Npgsql 10.x**.

> نستخدم PostgreSQL كقاعدة علائقية حقيقية ونستفيد من خصائصها عند الحاجة، بدون بناء Abstractions وهمية لمحركات أخرى.

> الأموال تستخدم `numeric`، واللحظات الزمنية تخزن UTC، والـIntegration Tests تعمل على PostgreSQL فعلية وليس EF InMemory.