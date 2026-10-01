# T01 - Backend Technology Stack

## القرار

الـBackend الخاص بـEduCenterOS سيُبنى باستخدام:

```text
.NET 10 LTS
ASP.NET Core 10
Entity Framework Core 10
C# 14

Target Framework:
net10.0
```

---

## إعدادات المشاريع

كل مشاريع الـBackend تستخدم:

```xml
<TargetFramework>net10.0</TargetFramework>
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
```

ويمنع استخدام `!` بصورة عشوائية لإخفاء Nullable warnings بدل معالجة السبب الحقيقي.

---

## إدارة نسخة الـSDK

يستخدم المشروع:

```text
global.json
```

لتوحيد إصدار .NET SDK بين:

- أجهزة أعضاء الفريق.
- الCI.
- بيئات الـBuild.

نثبت على `.NET 10` ونسمح بالترقية إلى أحدث Stable Patch متوافقة بعد نجاح الاختبارات.

---

## إدارة الـNuGet Packages

تدار نسخ الـPackages مركزيًا باستخدام:

```text
Directory.Packages.props
```

ولا توضع Package Versions بصورة متفرقة داخل كل Project إلا لسبب موثق.

يمنع خلط Major Versions غير المتوافقة مثل:

```text
EF Core 10
+
Provider Major Version غير متوافقة
```

---

## Stable Versions Only

داخل الـMain Solution يمنع استخدام:

```text
Preview SDK
Preview / RC Packages
LangVersion = preview
```

أي تجربة على نسخة Preview تتم خارج الـMain Branch ولا تصبح Dependency للمشروع قبل إصدار Stable واعتمادها.

---

## EF Core

`Entity Framework Core` هي أداة الوصول الأساسية للبيانات.

تستخدم في:

```text
Queries
Commands
Transactions
Migrations
Mappings
Indexes
Constraints
Optimistic Concurrency
```

ولا نضيف من البداية:

```text
Dapper
Micro ORM
Raw SQL layer
```

لمجرد توقع مشاكل أداء مستقبلية.

الخروج عن EF Core مسموح فقط عند وجود مشكلة حقيقية ومقاسة، مثل:

```text
Heavy reporting query
Large bulk operation
Complex query with demonstrated performance issue
```

ويكون الاستثناء محدودًا للحالة المطلوبة.

---

## استخدام C#

نستخدم Features اللغة عندما تحسن الوضوح، مثل:

```text
Records
Pattern Matching
Nullable Reference Types
Required Members
Collection Expressions
```

لكن:

> وضوح الكود أهم من استخدام أحدث Syntax.

ولا نستخدم Features جديدة لمجرد الاستعراض.

---

## القرارات خارج نطاق T01

الملف لا يحسم:

```text
Database Engine
Architecture
API Style
Authentication
Testing Stack
Logging / Observability
Caching
Messaging / Outbox
Docker
Hosting / Deployment
```

كل واحدة لها Technical Decision مستقلة.

---

## القرار النهائي المختصر

> EduCenterOS Backend يعتمد على `.NET 10 LTS` و`ASP.NET Core 10` و`EF Core 10` و`C# 14` مع `net10.0`.

> يتم تثبيت خط إصدار الـSDK باستخدام `global.json`، وإدارة الـNuGet Versions مركزيًا، واستخدام Stable Versions فقط.

> EF Core هي أداة الـPersistence الأساسية، وأي خروج عنها يحتاج مشكلة فعلية مثبتة وليس توقعًا مسبقًا.