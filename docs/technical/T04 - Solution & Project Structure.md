# T04 - Solution & Project Structure

## القرار

EduCenterOS سيستخدم:

> **Project واحدة لكل Business Module فعالة.**

لن نقسم كل Module إلى Projects منفصلة مثل:

```text
Academic.Domain
Academic.Application
Academic.Infrastructure
Academic.Api
```

بل:

```text
EduCenterOS.Modules.Academic
```

وتنظم داخليًا.

---

# 1. الشكل العام للـSolution

```text
EduCenterOS/
│
├── src/
│   ├── EduCenterOS.Api/
│   ├── EduCenterOS.BuildingBlocks/
│   │
│   └── Modules/
│       ├── IdentityAccess/
│       ├── Institutions/
│       ├── Subscriptions/
│       ├── Academic/
│       ├── Students/
│       ├── Enrollments/
│       └── ... active modules only
│
├── tests/
│   ├── EduCenterOS.UnitTests/
│   ├── EduCenterOS.IntegrationTests/
│   └── EduCenterOS.ArchitectureTests/
│
├── docs/
├── Directory.Build.props
├── Directory.Packages.props
├── global.json
└── EduCenterOS.sln
```

---

# 2. `EduCenterOS.Api`

الـAPI هي:

```text
Host
+
Composition Root
```

مسؤولة عن:

- تشغيل التطبيق.
- Configuration.
- تسجيل الـModules.
- Middleware.
- Endpoints registration.
- OpenAPI.
- Health Checks.
- Global exception handling.

ممنوع تحتوي على:

```text
Business Rules
Domain Entities
Business Repositories
EF Core mappings
Module migrations
```

---

# 3. `EduCenterOS.BuildingBlocks`

Project صغيرة للمفاهيم التقنية المشتركة فعلًا.

ممكن تحتوي على:

```text
Entity
AggregateRoot

DomainEvent

Result
Error

IClock

CorrelationId abstractions
Transaction abstractions
Event abstractions
```

لكن ممنوع تحتوي على Business Concepts مثل:

```text
Student
Institution
StudyGroup
Payment
Enrollment
Teacher
Permission الخاصة بموديول
```

قاعدة:

> `BuildingBlocks` ليست مكانًا لأي كود مش عارفين نحطه فين.

---

# 4. Business Module Project

كل Module فعالة يكون لها Project واحدة مثل:

```text
EduCenterOS.Modules.Academic/
│
├── Domain/
├── Features/
├── Infrastructure/
├── Contracts/
│
├── AcademicModule.cs
└── EduCenterOS.Modules.Academic.csproj
```

---

## `Domain`

تحتوي حسب الحاجة على:

```text
Entities
Aggregate Roots
Value Objects
Domain Rules
Domain Events
Domain Services
```

ولا تعتمد على Infrastructure.

---

## `Features`

تنظم الـUse Cases كـVertical Slices:

```text
Features/
├── CreateGroup/
├── ChangePrimaryTeacher/
├── ChangeGroupCapacity/
└── GetGroupDetails/
```

---

## `Infrastructure`

تحتوي التفاصيل التقنية الخاصة بالموديول، مثل:

```text
DbContext
EF Core Configurations
Migrations
Persistence implementations
External provider implementations
```

عندما تحتاج الـModule لها.

---

## `Contracts`

تحتوي فقط على الأشياء التي تحتاج Module أخرى إلى معرفتها.

مثل:

```text
Public DTOs
Queries / Commands المعلنة
Integration Events
Public Interfaces الضرورية
```

ولا نضع Domain Entities نفسها كـContracts.

---

# 5. الوصول للـTypes

القاعدة الافتراضية:

```csharp
internal
```

أي Type داخل Module تظل `internal` إلا لو محتاجة فعلًا للخروج خارج الـAssembly.

المسموح أن يكون `public` مثلًا:

```text
Module registration entry point
Endpoint registration entry point
Public contracts
Necessary public interfaces
```

ممنوع جعل Domain Entity `public` فقط لتسهيل استخدامها من Module أخرى.

---

# 6. Project References

الاتجاه الأساسي:

```text
EduCenterOS.Api
├── Active Module Projects
└── EduCenterOS.BuildingBlocks

Module Project
└── EduCenterOS.BuildingBlocks

EduCenterOS.BuildingBlocks
└── No Business Module
```

القواعد:

1. `BuildingBlocks` لا تشير لأي Module.
2. Module لا تشير إلى `Api`.
3. `Api` تسجل الـModules لأنها Composition Root.
4. Direct reference بين Modules ليست Default.
5. أي Module reference تحتاج سببًا واضحًا.
6. ممنوع Circular References.
7. طريقة التواصل التفصيلية بين Modules لها قرار مستقل.

---

# 7. تسجيل الـModules

كل Module لها Entry Points واضحة مثل:

```text
AddAcademicModule(...)
MapAcademicEndpoints(...)
```

ويكون التسجيل صريحًا.

لا نبدأ بـ:

```text
Complex reflection
Magic assembly scanning
Plugin system
```

بدون احتياج.

---

# 8. Test Projects

نبدأ بثلاثة Projects فقط.

## Unit Tests

```text
EduCenterOS.UnitTests
```

للـ:

- Domain rules.
- Value objects.
- Aggregate behavior.
- Isolated handlers.

وتنظم الاختبارات داخليًا حسب الـModule.

---

## Integration Tests

```text
EduCenterOS.IntegrationTests
```

للـ:

- Database.
- APIs.
- Authorization.
- Transactions.
- Concurrency.
- Module integration.
- Tenant isolation.

---

## Architecture Tests

```text
EduCenterOS.ArchitectureTests
```

تتحقق من:

- عدم كسر Module boundaries.
- عدم وجود Circular dependencies.
- Domain لا تعتمد على Infrastructure.
- عدم وجود Business Logic داخل `Api`.
- عدم تسريب `internal` types.
- عدم وجود Business Entities داخل `BuildingBlocks`.

---

# 9. إنشاء Module Projects

الـBusiness Documentation تعرف **15 Core V1 Modules**.

لكن:

> لا ننشئ 15 `.csproj` فارغين من أول يوم.

Project الـModule تنشأ عند بدء أول Feature فعلية تخصها.

مثال لبداية المشروع:

```text
IdentityAccess
Institutions
Subscriptions
Academic
Students
Enrollments
```

ثم تضاف الباقي حسب ترتيب التنفيذ.

---

# 10. Marketplace Phase 2

الموديولات:

```text
Marketplace
MarketplaceFinance
```

موثقة Business-wise، لكنها **ليست Core V1**.

لذلك لا ننشئ لها الآن:

```text
Projects
DbContexts
Schemas
Migrations
Endpoints
```

يتم إنشاؤها عند بدء Phase 2 فعليًا.

---

# 11. قواعد أساسية

1. Project واحدة لكل Business Module فعالة.
2. لا نفصل Layers إلى Projects مستقلة داخل كل Module.
3. `Api` Host فقط.
4. `BuildingBlocks` صغيرة وتقنية.
5. Types تكون `internal` افتراضيًا.
6. كل Module تملك Infrastructure الخاصة بها عند الحاجة.
7. Module لا تستخدم DbContext الخاصة بـModule أخرى.
8. ممنوع Circular Project References.
9. تسجيل الـModules صريح.
10. لا ننشئ Projects فارغة قبل وجود Feature فعلية.
11. كل Feature تنظم كـVertical Slice.
12. Architecture boundaries تتغطى باختبارات.

---

# القرار لا يحسم

يتحدد في قرارات منفصلة:

```text
Minimal APIs / Controllers
Module communication
Cross-module contracts
Events
Outbox
DbContexts
Schemas
Foreign Keys
Transactions
Concurrency
```

---

# القرار النهائي المختصر

> EduCenterOS يستخدم **Project واحدة لكل Business Module فعالة**، بالإضافة إلى `Api` كـHost و`BuildingBlocks` صغيرة.

> كل Module تحتوي `Domain`, `Features`, `Infrastructure`, و`Contracts` داخليًا حسب الحاجة.

> الأنواع `internal` افتراضيًا، والـDependencies تكون واضحة وبدون Circular References.

> لا يتم إنشاء Module Project إلا عند بدء Feature فعلية تخصها، و`Marketplace` و`MarketplaceFinance` لا تنشأ لهما Projects قبل Phase 2.