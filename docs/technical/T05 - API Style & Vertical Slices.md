# T05 - API Style & Vertical Slices

## القرار

EduCenterOS سيستخدم:

```text
Minimal APIs
+
Route Groups per Module
+
Vertical Slice per Use Case
+
Direct Handler Invocation
+
Result / Result<T>
+
Manual Mapping
+
No MediatR
```

---

# 1. Minimal APIs

Minimal APIs هي الاختيار الافتراضي للـBusiness APIs.

كل Module تسجل Routes الخاصة بها من خلال نقطة واضحة مثل:

```text
MapAcademicEndpoints(...)
MapEnrollmentsEndpoints(...)
```

ولا يتم وضع جميع الـEndpoints داخل `Program.cs`.

استخدام Controllers يحتاج سبب تقني حقيقي وموثق.

---

# 2. Vertical Slice لكل Use Case

كل Use Case تتنظم داخل Feature مستقلة.

مثال:

```text
Features/
└── CreateGroup/
    ├── Endpoint.cs
    ├── Request.cs
    ├── Response.cs
    ├── Handler.cs
    └── Validator.cs
```

مش لازم كل Feature تحتوي كل الملفات.

الهدف:

> تنظيم الكود حسب الـUse Case، وليس فرض Template جامدة.

---

# 3. مسؤولية الـEndpoint

الـEndpoint مسؤولة عن HTTP فقط:

- Route.
- HTTP method.
- Request binding.
- Authorization metadata.
- استدعاء الـHandler.
- تحويل النتيجة إلى HTTP response.
- OpenAPI metadata.
- تمرير `CancellationToken`.

ممنوع داخلها:

```text
Business Rules
Database Queries المعقدة
SaveChanges
Financial calculations
Aggregate mutations
```

---

# 4. مسؤولية الـHandler

الـHandler تنسق الـUse Case.

مسؤولة عن:

- تحميل البيانات المطلوبة.
- تنفيذ Application checks.
- استدعاء Domain methods.
- استخدام Infrastructure الخاصة بالموديول.
- حفظ التغييرات.
- إعادة `Result` أو `Result<T>`.

ولا تعتمد على:

```text
HttpContext
IResult
IActionResult
HTTP Status Codes
Headers / Cookies مباشرة
```

---

# 5. Result مستقل عن HTTP

الـHandler لا تعرف معنى:

```text
404
409
422
```

هي ترجع Error/Result يعبر عن المشكلة.

مثال:

```text
GroupNotFound
GroupCodeAlreadyExists
SeatNoLongerAvailable
```

ثم HTTP Layer تحولها إلى الاستجابة المناسبة.

تصميم `Result`, `Error`, و`ProblemDetails` له قرار مستقل.

---

# 6. Request & Response Models

كل Endpoint تستخدم Models خاصة بها.

ممنوع استخدام:

```text
Domain Entity
EF Core Entity
Database Model
```

كـRequest أو Response مباشرة.

مثال:

```text
CreateGroupRequest
CreateGroupResponse
```

ولو Model مطلوبة بين Modules:

> تتحول إلى Contract صريحة، ولا نعيد استخدام HTTP DTO عشوائيًا.

---

# 7. Mapping

الاختيار الافتراضي:

```text
Manual Mapping
```

مثال:

```csharp
var response = new GroupResponse(
    group.Id,
    group.DisplayCode,
    group.Name);
```

لا نضيف:

```text
AutoMapper
Mapster
```

إلا لو ظهر احتياج حقيقي ومتكرر يبرر ذلك.

---

# 8. Commands & Queries

نستخدم فصلًا منطقيًا بسيطًا.

## Commands

تغير حالة النظام:

```text
CreateGroup
ConfirmEnrollment
RecordPayment
CancelSession
```

## Queries

للقراءة:

```text
GetGroupDetails
SearchStudents
GetStudentStatement
```

ده لا يعني تطبيق CQRS infrastructure معقدة.

لا نستخدم:

```text
Separate read/write databases
Command Bus
Event Sourcing
```

لمجرد وجود Commands وQueries.

---

# 9. MediatR

لن نستخدم MediatR في Core V1.

التدفق الطبيعي:

```text
Endpoint
↓
Handler
```

والـHandler يتم حقنها مباشرة باستخدام Dependency Injection.

السبب:

- أوضح.
- أقل Dependencies.
- أسهل في Debugging.
- مناسب لحجم الفريق.
- لا نحتاج Pipeline عامة حاليًا.

---

# 10. Cross-Cutting Concerns

بدل MediatR Pipeline Behaviors نستخدم الأداة المناسبة حسب المشكلة:

```text
Middleware
→ cross-cutting HTTP concerns

Endpoint Filters
→ endpoint-specific HTTP concerns

Decorators
→ application behavior عند وجود حاجة

EF Core Interceptors
→ persistence concerns

Domain
→ business invariants
```

---

# 11. Handler Registration

التسجيل صريح افتراضيًا.

مثال:

```csharp
services.AddScoped<CreateGroupHandler>();
services.AddScoped<ConfirmEnrollmentHandler>();
```

لا نبدأ بـ:

```text
Complex reflection
Magic registration
Large assembly scanning
```

إلا لو أصبح التسجيل اليدوي مشكلة فعلية.

---

# 12. CancellationToken

أي I/O Async يجب أن يمرر:

```csharp
CancellationToken
```

من:

```text
Endpoint
↓
Handler
↓
Database / Provider / File operation
```

إلا لو يوجد سبب واضح لعدم استخدامه.

---

# 13. Typed Results

يفضل استخدام Typed Results في طبقة HTTP عندما تحسن:

- وضوح الـEndpoint.
- OpenAPI.
- الاختبارات.
- تحديد الاستجابات الممكنة.

لكنها تظل مسؤولية HTTP Layer فقط.

---

# 14. Validation

T05 يحدد فقط الفصل بين أنواع الـValidation.

```text
Input Validation
→ قبل تنفيذ Use Case

Business Rules
→ Domain / Application logic

Database Constraints
→ Final consistency defense
```

اختيار:

```text
FluentValidation
Built-in validation
ProblemDetails format
```

يتم في قرار مستقل.

---

# 15. خارج نطاق T05

الملف لا يحسم:

```text
URL conventions
API versioning

Pagination
Filtering
Sorting

Validation library
ProblemDetails

Authorization policies

Transactions
Concurrency

Idempotency

Domain Events
Integration Events
Outbox

Module communication details
```

كل واحدة تتحدد في القرار التقني المناسب.

---

# القواعد النهائية

1. Minimal APIs هي Default.
2. كل Use Case لها Vertical Slice.
3. الـEndpoint مسؤولة عن HTTP فقط.
4. الـHandler تنسق الـUse Case.
5. الـDomain تحمي Business invariants.
6. Request/Response منفصلة عن Domain وEF Entities.
7. الـHandler ترجع `Result` مستقل عن HTTP.
8. Manual Mapping هي Default.
9. Commands وQueries فصل منطقي فقط.
10. لا نستخدم MediatR في Core V1.
11. الـHandlers تستدعى مباشرة عن طريق DI.
12. `CancellationToken` يمر خلال I/O async.
13. لا نضيف Abstraction أو Library بدون مشكلة فعلية تبررها.

---

# القرار النهائي المختصر

> EduCenterOS يستخدم **Minimal APIs + Vertical Slices**، وكل Endpoint تظل رفيعة وتستدعي Handler مباشرة.

> الـHandlers لا تعتمد على HTTP، وتعيد `Result` مستقلًا عنه، مع Request/Response Models منفصلة عن الـDomain والـPersistence.

> لا نستخدم MediatR أو AutoMapper في Core V1؛ Direct Invocation وManual Mapping هما الاختيار الافتراضي.