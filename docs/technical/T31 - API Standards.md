# T31 - API Standards

## الهدف من القرار

تحديد القواعد الموحدة لتصميم واستهلاك HTTP APIs داخل EduCenterOS، بحيث:

- تكون الـContracts واضحة وثابتة.
- تتوافق مع Minimal APIs وVertical Slices.
- لا تتسرب Module boundaries إلى Public URLs.
- تحافظ على Multi-Tenancy وAuthorization boundaries.
- يكون التعامل مع IDs وPagination وErrors وFiles موحدًا.
- تكون Idempotency وSecurity requirements معلنة بوضوح.
- يستطيع OpenAPI وصف الـAPI بصورة موثوقة.
- لا تتحول الـHTTP concerns إلى Domain/Application concerns.

---

# القرار النهائي

يعتمد EduCenterOS:

```text
RESTful HTTP API
+
URL Versioning using /api/v1
+
Resource-oriented Routes
+
Lowercase kebab-case route segments
+
Plural Resource Naming
+
Internal UUIDs as default resource identifiers
+
Standard HTTP resource methods where appropriate
+
POST Business Action Routes for explicit domain commands
+
GET/HEAD with safe read semantics
+
RFC 9457 Problem Details using application/problem+json
+
UTF-8 camelCase JSON for normal API contracts
+
Explicit controlled exceptions for file transport
+
Explicit Hybrid Multi-Tenancy
+
Dedicated PublicRead routes
+
X-Correlation-Id on responses
+
Selective Idempotency according to T17
+
Mandatory OpenAPI metadata
+
Explicit endpoint security classification, including AnonymousSecurity vs PublicRead
+
Deterministic page/cursor pagination contracts
+
Explicit HTTP success status and Location semantics
```

---

# 1. API Versioning

كل Business/Public application HTTP endpoints تبدأ بـ:

```text
/api/v1/...
```

مثال:

```http
GET /api/v1/me
GET /api/v1/institutions/{institutionId}/groups
```

لا نخلط داخل نفس الإصدار بين:

```text
/v1
/api/v1
/api
```

الـbaseline:

```text
/api/v1
```

Operational host endpoints مثل readiness/liveness/metrics ليست Business API ولا تدخل هذا namespace أو الـpublic OpenAPI document؛ مكانها وحمايتها يتبعان T34/T35/Deployment decisions.

---

# 2. Versioning Strategy

الـAPI major version تظهر في URL.

```text
/api/v1
```

Minor implementation changes لا تنشئ:

```text
/api/v1.1
```

إصدار جديد يستخدم فقط عند وجود breaking HTTP contract يستحق:

```text
/api/v2
```

ولا ننشئ V2 لمجرد تغيير داخلي في Domain أو Database.

---

# 3. URLs تعبر عن Resources وليس Modules

الـURL لا تعرض Internal Architecture.

صحيح:

```http
/api/v1/institutions/{institutionId}/groups
```

وليس:

```http
/api/v1/academic/groups
```

`Academic` اسم Module داخلي.

الـClient تتعامل مع:

```text
Group
Student
Payment
Enrollment
```

وليس أسماء Projects أو Schemas.

---

# 4. Resource Naming

نستخدم plural resource nouns.

مثال:

```text
/students
/groups
/enrollments
/payments
/institutions
/branches
```

ونتجنب:

```text
/getStudents
/createGroup
/processPayment
```

في resource operations الطبيعية.

كل static route segments تكون lowercase `kebab-case`. أسماء JSON/query parameters تكون `camelCase`. Route parameter names في OpenAPI تكون `camelCase` أيضًا، حتى لو C# parameter استخدمت PascalCase داخليًا.

---

# 5. IDs في URLs

الـdefault للـauthenticated operational APIs:

```text
Internal UUID
```

مثال:

```http
GET /api/v1/institutions/{institutionId}/groups/{groupId}
```

طبقًا لـT10، الـID:

```text
Identifier
≠ Security mechanism
```

معرفة UUID صحيحة لا تمنح access.

HTTP representation تستخدم lowercase canonical `D` format (`xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`). `Guid.Empty` وnon-canonical/malformed values تفشل request validation قبل lookup. لا نستخدم قبول ID كدليل أن الـresource موجودة أو مسموح بها.

---

# 6. SystemCode / Public Slug Exceptions

Internal UUID ليست قانونًا مطلقًا لكل Public/Human-facing route.

لو Business Feature تحتاج identifier stable/public مثل:

```text
SystemCode
PublicSlug
```

يمكن استخدامه بقرار صريح لذلك الـresource.

مثال مفاهيمي:

```text
/public/institutions/{publicSlug}
```

لكن:

```text
DisplayCode
```

القابل للتعديل بواسطة المستخدم لا يصبح authoritative identifier افتراضيًا.

---

# 7. DisplayCode

`DisplayCode` تستخدم للعرض والبحث العملي حسب Domain.

مثال:

```json
{
  "id": "019...",
  "displayCode": "G-MATH-12"
}
```

لكن الـdefault resource identity تظل الـInternal ID.

---

# 8. HTTP Methods — Queries

Queries التي لا تطلب Business mutation تستخدم:

```http
GET
HEAD
```

مثل:

```http
GET /api/v1/me

GET /api/v1/institutions/{institutionId}/groups

GET /api/v1/institutions/{institutionId}/students/{studentId}
```

GET/HEAD لا تستخدم لتنفيذ Business mutation. الـincidental observability مثل access logs/metrics لا تغير safe semantics، لكن لا يجوز أن تعتمد الـBusiness على side effect من safe request.

HEAD، عندما تعرضها Feature، تستخدم نفس authorization/status/headers الخاصة بالـGET المقابلة ولا ترسل response body. لا نفرض HEAD لكل resource بلا Use Case.

---

# 9. Create Resource

إنشاء Resource طبيعي يستخدم:

```http
POST /collection
```

مثال:

```http
POST /api/v1/institutions/{institutionId}/groups
```

وليس:

```text
POST /groups/create
```

لو العملية هي فعلًا Resource creation عادية.

نجاح الإنشاء يعيد:

```text
201 Created
Location: /api/v1/.../{newResourceId}
Response DTO أو minimal created-resource reference
```

`Location` تكون URI للـprimary resource الجديدة، والـIdempotent replay تعيد نفس status/resource identity طبقًا لـT17.

---

# 10. PUT / PATCH

`PUT` تستخدم فقط عندما تمثل Complete replacement للـresource representation المعروفة للـClient، وتكون idempotent حسب HTTP semantics. لا نستخدمها كـpartial update أو Upsert ضمني بلا Contract صريحة.

`PATCH` تستخدم لـpartial update من خلال Explicit feature-owned patch DTO. لا نعتمد Generic JSON Patch أو arbitrary property paths في Core V1؛ أي دعم مستقبلًا يحتاج قرارًا أمنيًا وvalidation مستقلة.

Simple editable profile/configuration يمكن أن تستخدم إحداهما حسب semantics الحقيقية، لكن لا نحول Business workflow معقدة إلى generic PATCH لمجرد أنها تغير property.

نجاح update يعيد `200` مع representation/result عندما يوجد body مفيد، أو `204 No Content` بدون body. لا نرسل body مع `204`.

---

# 11. Business Commands

State transitions أو domain commands واضحة تستخدم:

```text
POST Business Action Route
```

مثال:

```http
POST /api/v1/institutions/{institutionId}/groups/{groupId}/change-primary-teacher

POST /api/v1/institutions/{institutionId}/enrollments/{enrollmentId}/withdraw

POST /api/v1/institutions/{institutionId}/payments/{paymentId}/reverse
```

الفكرة:

```text
Domain Command
→ explicit action
```

بدل إخفائها داخل generic property patch.

الـsynchronous command تعيد:

```text
200 OK
→ عند وجود Result DTO مفيدة

204 No Content
→ عند نجاح بلا response body
```

لو command أنشأت Resource جديدة فعلًا تستخدم `201 + Location`. لو قبلت Job لم تكتمل تستخدم `202` وفق القسم 61، وليس `200` يوحي بأن العمل اكتمل.

---

# 12. DELETE

تستخدم فقط عندما Semantics العملية فعلًا deletion.

لكن Historical/Financial resources التي طبقًا لـT06 لا تُحذف:

```text
Payment
Enrollment history
Attendance history
Settlement
```

تستخدم Commands مثل:

```text
Cancel
Reverse
Withdraw
Archive
```

وليس HTTP DELETE لتدمير التاريخ.

نجاح DELETE الفعلية يعيد `204` افتراضيًا أو `200` لو توجد Result DTO موثقة. repeated DELETE semantics تحددها الـFeature كـSemanticallyIdempotent أو `NotRequiredWithReason` طبقًا لـT17؛ لا نضيف persistent idempotency تلقائيًا.

---

# 13. Route Nesting

Nested routes تستخدم عندما Parent context جزء مهم من الـresource access.

مثال:

```http
/api/v1/institutions/{institutionId}/groups
```

لكن نتجنب nesting عميقة مثل:

```text
/institutions/{id}
/branches/{id}
/years/{id}
/groups/{id}
/students/{id}
/payments/{id}
```

الـresource التي لها Identity مستقلة لا تحتاج عرض كل graph في URL.

---

# 14. Multi-Tenancy Model

نعتمد:

```text
Explicit Hybrid Multi-Tenancy
```

عند Institution-scoped operations:

```http
/api/v1/institutions/{institutionId}/...
```

لكن:

```text
institutionId from route
=
Requested Institution Candidate
```

وليست:

```text
Trusted Tenant Context
```

---

# 15. Institution Context Flow

المسار:

```text
Route InstitutionId
↓
Untrusted candidate
↓
Current Actor
↓
Resolve required Access Relationship
↓
Authorization / Eligibility
↓
Validated IInstitutionContext
↓
Business Use Case
```

طبقًا لـT09/T14.

---

# 16. InstitutionScoped لا تعني Staff فقط

`InstitutionScoped` Endpoint لا تفترض تلقائيًا:

```text
InstitutionMembership
```

الوصول قد يكون عبر:

```text
StaffMembership
Student
Guardian
PlatformSupport
```

حسب T14.

لذلك:

```text
Institution Context
≠ Staff Membership
```

---

# 17. User-scoped Routes

الحاجات المرتبطة بالحساب الحالي تستخدم Routes مثل:

```http
/api/v1/me
/api/v1/me/institutions
```

ولا نحتاج `institutionId` لو الـUse Case نفسها غير Institution-scoped.

`me` singleton alias استثناء مقصود من plural-resource rule. لا نخلطها مع `/account/me` أو `/my-*` routes في V1.

---

# 18. Platform-scoped Routes

PlatformAdministration operations تستخدم Namespace واضح منفصل عن Institution business endpoints.

الشكل الدقيق يتحدد حسب Feature.

لكن ممنوع تمثيل Platform Admin كأنه:

```text
InstitutionId = *
```

أو:

```text
All tenants context
```

طبقًا لـT14.

---

# 19. PublicRead APIs

`PublicRead` endpoints تستخدم:

```text
/api/v1/public/...
```

مثلًا مستقبلًا:

```text
/api/v1/public/institutions/...
```

`PublicRead` route:

```text
≠ bypass normal internal endpoint
```

لها:

```text
Dedicated DTO
Dedicated query
Visibility rules
Minimal public fields
```

طبقًا لـT09/T14/T35.

`PublicRead` هنا تعني Business data مقصود إتاحتها Anonymous. لا تشمل Login/Refresh/Verification/Recovery؛ هذه `AnonymousSecurity` وتستخدم namespace مثل:

```text
/api/v1/auth/...
```

ولا توضع تحت `/public` لأنها ليست Public data surface.

---

# 20. PublicRead vs Authenticated Data

وجود Resource في النظام لا يعني وجود Public endpoint لها.

Public exposure تحتاج Business decision واضحة.

مثل:

```text
DirectLinkOnly
PublicSearch
Hidden
```

تظل visibility semantics في الـowning Module.

نستخدم المصطلحات التالية بدون خلط:

```text
Business/Public application HTTP API
→ أي application contract خارجية تحت /api/v1

PublicRead
→ anonymous business data explicitly exposed under /public

AnonymousSecurity
→ anonymous authentication/onboarding security flow under /auth
```

---

# 21. Pagination

القوائم التي يمكن أن تكبر يجب أن تستخدم Pagination.

الـdefault:

```http
?page=1&pageSize=20
```

Page pagination مناسبة للقوائم الإدارية العادية، وقواعد Core V1:

```text
page     → integer, minimum 1, default 1
pageSize → integer, minimum 1, default 20, maximum 100
```

Endpoint يمكن أن تعتمد maximum أصغر. Maximum أكبر من `100` يحتاج استثناء موثق واختبار أداء؛ exports الكبيرة لا تنفذ برفع pageSize بل عبر Export/Job contract.

---

# 22. Cursor Pagination

للبيانات الكبيرة أو flows التي تحتاج stable forward navigation:

```http
?cursor=<opaque-value>&limit=50
```

يمكن استخدام Cursor Pagination.

قواعد Core V1:

```text
limit → integer, minimum 1, default 50, maximum 100
cursor → optional on first request, exactly one opaque value afterward
```

الـcursor تكون Versioned وintegrity-protected، ومربوطة على الأقل بالـendpoint/Trusted Scope والfilters والsort والـlast ordering tuple. لا تحتوي Secrets أو PII قابلة للقراءة، ولا تعتبر Authorization؛ كل request تعيد T09/T14 checks. Cursor malformed/tampered أو غير مطابقة للquery الحالية تفشل Validation ولا تعاد كبداية جديدة صامتة.

Core V1 تستخدم ASP.NET Core Data Protection بpurpose string ثابتة ومختلفة لكل cursor contract لإنتاج Base64Url opaque token؛ لا نكتب Cryptography بأنفسنا. Payload تحتوي version + binding hash + ordering tuple فقط، وإدارة/استمرار key ring تتبع T36/Deployment.

Cursor endpoint تستخدم deterministic total ordering؛ تضيف `id` كـfinal tie-breaker إذا لم يكن sort المختار unique.

---

# 23. لا نخلط Pagination Schemes

Endpoint واحدة تستخدم Contract واحدة.

ممنوع:

```text
?page=
&cursor=
&offset=
```

كلهم لنفس Endpoint بلا سبب.

وجود `page` مع `cursor` أو تكرار scalar pagination parameter أكثر من مرة يفشل Validation.

---

# 24. Server-side Pagination Limits

Client لا تحدد حجمًا غير محدود.

كل paginated endpoint لها:

```text
Default size
Maximum size
```

server-side.

مثال:

```text
pageSize requested = 500000
→ rejected or capped according to endpoint contract
```

Core V1 maximum العامة `100` كما سبق، ويمكن للـFeature تخفيضها. رفعها استثناء موثق وليس default.

---

# 25. Pagination Response

Page pagination تستخدم:

```json
{
  "items": [],
  "pagination": {
    "type": "page",
    "page": 1,
    "pageSize": 20,
    "totalCount": 125,
    "totalPages": 7
  }
}
```

Cursor pagination تستخدم:

```json
{
  "items": [],
  "pagination": {
    "type": "cursor",
    "nextCursor": "opaque-or-null",
    "hasMore": true
  }
}
```

`nextCursor = null` و`hasMore = false` عند النهاية. Cursor pagination لا تعد `totalCount` أو page numbers؛ لو الـUse Case تحتاج count إداري دقيق تستخدم Page contract أو Endpoint إحصائية منفصلة.

Pagination لا تعني Database snapshot عبر عدة requests. Page pagination قد ترى drift مع concurrent writes؛ لو الـUse Case تحتاج forward navigation أكثر ثباتًا تستخدم Cursor ordering، ولو تحتاج frozen export/report تستخدم Job/Snapshot design متخصصة.

---

# 26. Single Resource Response

العمليات الفردية لا تحتاج generic envelope.

مثال:

```json
{
  "id": "019..."
}
```

وليس:

```json
{
  "data": {
  }
}
```

إلا لو ظهر احتياج Contract حقيقي مستقبلًا.

---

# 27. Filtering

Filtering تستخدم parameters صريحة.

مثال:

```http
?status=Active&gradeId=...
```

Request Model تقوم بـ:

```text
Parsing
Validation
Mapping
```

---

# 28. Search

Search parameter يمكن أن يكون:

```http
?search=ahmed
```

لكن معنى البحث تحدده Feature.

لا نفترض generic full-text search behavior لكل resources.

كل Feature توثق fields التي يشملها البحث والnormalization والحد الأقصى لطول `search`. القيمة تعامل كdata parameterized؛ لا تتحول إلى SQL/Full-text syntax من Client.

---

# 29. Sorting

الشكل الموحد في Core V1:

```text
?sort=createdAt:desc,id:asc
```

القواعد:

```text
field:direction
multiple fields separated by comma
direction = asc | desc
maximum 3 explicit sort fields
```

كل Endpoint توثق default sort. Pagination تضيف `id` كـfinal deterministic tie-breaker إن لم يرسله Client؛ ولا تغير معنى sort الأساسية.

لا نسمح arbitrary property/expression من Client تتحول مباشرة إلى Dynamic LINQ.

---

# 30. Filter / Sort Allowlist

كل Endpoint تحدد:

```text
Allowed filters
Allowed sort fields
Allowed directions
```

Client لا ترسل field arbitrary يؤدي Reflection/Dynamic SQL behavior.

Unknown filter/sort field أو direction غير صالحة تفشل Validation بدل التجاهل الصامت. Mapping من public field code إلى expression مكتوبة Server-side؛ لا Reflection over arbitrary property names ولا SQL fragments.

---

# 31. Request DTOs

كل mutation تستخدم Explicit Request DTO.

ممنوع:

```text
Bind HTTP body directly to EF Entity
```

وده يحمي من:

```text
Mass Assignment
Hidden field updates
Tenant ID injection
Security-sensitive property modification
```

والأنواع منفصلة صراحة:

```text
HTTP Request/Response DTO
≠ T18 Cross-module Contract DTO
≠ Domain Entity / Value Object persistence shape
```

Endpoint تعمل mapping إلى Application command/query صريحة؛ لا تمرر HTTP DTO نفسها عبر Modules كـBusiness contract.

---

# 32. Response DTOs

الـAPI لا ترجع Domain/EF Entity مباشرة.

تستخدم DTO/Projection تحتوي فقط الحقول المطلوبة.

خصوصًا:

```text
PII
Security state
Financial state
Internal audit metadata
```

لا تظهر بدون حاجة.

الـClient يجب أن تتجاهل additive response fields التي لا تعرفها. الـServer لا تعيد استخدام response DTO كinput DTO تلقائيًا.

---

# 33. Content Type — Normal APIs

الـnormal API request/response contract:

```text
Request Content-Type: application/json; charset=utf-8
Successful JSON response: application/json; charset=utf-8
Problem response: application/problem+json
```

`charset` يمكن حذفه لو framework ترسل UTF-8 افتراضيًا، لكن JSON encoding المقبولة في Core V1 هي UTF-8. Body بنوع غير مدعوم تفشل `415 Unsupported Media Type`، و`Accept` لا تسمح بأي representation مدعومة تفشل `406 Not Acceptable`.

## 33.1 JSON Serialization Contract

قواعد Core V1:

```text
Property names        → camelCase, case-sensitive
Enums / stable codes  → explicit strings, never ordinal numbers
UUID output           → lowercase canonical D format
Instant               → T11 ISO 8601 with Z/explicit offset; output normalized to UTC
Date                  → YYYY-MM-DD
Local time            → HH:mm:ss
Decimal               → JSON number using invariant representation
Money amount          → canonical decimal string; see section 66
Nulls                 → serialized explicitly by default
Unknown request field → rejected for mutation DTOs
```

لو field تسمح Missing و`null` بمعنيين مختلفين، Request DTO تمثل presence صراحة؛ لا نعتمد على nullable property وحدها لتخمين الفرق. Unknown enum/code تفشل Validation، ولا تتحول للقيمة الرقمية أو default enum member.

Query/header scalar parameter المطلوبة مرة واحدة ترفض multiple values، إلا لو الـContract نفسها Collection موثقة. Unknown query parameters ترفض في Business endpoints حتى لا تمر typos بصمت؛ infrastructure parameters المسموحة مثل tracing لا تدخل binding الخاصة بالFeature.

---

# 34. File Upload Exception

`JSON Only` لا تعني أن رفع الملفات مستحيل.

Feature-owned direct upload يمكن أن تستخدم:

```text
multipart/form-data
```

إذا القرار المتخصص يسمح بذلك.

---

# 35. Large File Uploads

للملفات الكبيرة نفضل عند اعتماد File Storage architecture:

```text
Presigned upload flow
```

بدل تمرير binary كبير داخل الـAPI process.

T23/T35 تحددان:

```text
Validation
Scanning
File type policy
Storage
Access control
Retention
```

T31 تحسم HTTP contract فقط.

---

# 36. Download Contracts

File downloads الكبيرة لا يلزم أن تكون JSON.

يمكن أن تستخدم:

```text
Presigned download
Streaming
Redirect to controlled storage endpoint
```

حسب T23.

---

# 37. ProblemDetails

Errors تستخدم RFC 9457 Problem Details contract مع:

```http
Content-Type: application/problem+json
```

الـHTTP error body الموحد يحتوي مفاهيم مثل:

```json
{
  "type": "urn:educenteros:problem:validation",
  "title": "Validation failed",
  "status": 400,
  "code": "Validation.Failed",
  "detail": "One or more request values are invalid.",
  "instance": "urn:educenteros:problem-instance:<opaque-id>",
  "correlationId": "<response-correlation-id>",
  "traceId": "<w3c-activity-trace-id>",
  "errors": {}
}
```

لكن:

> T31 تحدد الشكل الخارجي العام فقط.

التصنيف والمapping التفصيلي ملك T32.

القواعد التي لا تنتظر T32:

- `type` stable absolute URI/URN تعرف problem type، أو `about:blank` عند عدم وجود semantics إضافية؛ لا تكون empty string.
- `status` تطابق HTTP response status الفعلية.
- `title` ثابتة لنفس type إلا عند localization.
- `detail` occurrence-specific وموجهة للClient، ولا تحمل debugging internals.
- `instance` optional؛ لو موجودة تكون opaque URI reference للواقعة، وليست stack/log path.
- `code` extension هي machine-readable EduCenterOS contract التي تعتمدها Clients حسب T32.
- `errors` تظهر فقط عندما problem type تعرف validation errors، وشكلها النهائي يثبت في T32.
- Clients تتجاهل extension members غير المعروفة.

---

# 38. Error Code

Client تعتمد على:

```text
code
```

وليس نص `detail`.

الـError Code stable contract طبقًا لـT32.

---

# 39. Sanitized Errors

HTTP contract لا يكشف:

```text
Stack Trace
SQL Error
Exception Message
Secret
Credential
Internal topology
```

التفاصيل التشغيلية تذهب للObservability وليس الـClient.

---

# 40. Correlation ID

كل HTTP response ترجع:

```text
X-Correlation-Id
```

ويظهر أيضًا في ProblemDetails عند الخطأ.

ProblemDetails تستخدم `correlationId` لنفس قيمة `X-Correlation-Id`. `traceId` قيمة W3C Activity مستقلة يولدها السيرفر وقد تختلف؛ لا نستخدم الاسمين لنفس المفهوم.

---

# 41. Correlation ID ليست Security Input

حتى لو سمحنا للClient بتقديم Correlation ID:

```text
Client value
→ validated/normalized according to observability policy
```

لكنها لا تدخل:

```text
Authentication
Authorization
Tenant selection
Idempotency
Business decision
```

طبقًا لـT35.

التوليد والتتبع الكامل يتبع T34.

---

# 42. Idempotency

Idempotency لا تطبق على كل Endpoint.

تتبع T17.

Strong candidates:

```text
Bookings
Enrollments / Renewals where applicable

Payments
Refunds

Sensitive create commands
External-integration commands
Long-running job creation
```

Core V1 baseline لا تعاد صياغتها في T31 بصورة مختلفة: `RecordCashPayment` والrefund/material financial posting وmaterial booking/enrollment/job creation والhigh-risk ticket-bound mutation تكون `Required` حسب T17.

---

# 43. Idempotency-Key

عندما Endpoint تتطلبها:

```http
Idempotency-Key: <canonical-uuid>
```

قواعد HTTP binding:

```text
Exactly one header value
Canonical UUID text
Non-empty
00000000-0000-0000-0000-000000000000 forbidden
Raw value never logged
```

T31 مسؤولة عن:

```text
HTTP Header
OpenAPI declaration
```

T17 مسؤولة عن:

```text
Scope
Fingerprint
Persistence
Replay
Expiry
Concurrency
```

---

# 44. Idempotency Endpoint Metadata

كل mutation يتم تقييمها كواحدة من:

```text
Required
SemanticallyIdempotent
SecuritySpecific
NotRequiredWithReason
```

ولا نضيف Global Middleware تعطي نفس behavior لكل operations.

كل Endpoint في Mode = `Required` تعلن في version-controlled metadata نفسها:

```text
OperationCode
OperationIdSource = ServerGeneratedAtClaim | CallerSupplied
FingerprintContractVersion
ResultContractVersion
RetentionProfile
RequiresOperationAuthorizationTicket
```

وتوثق OpenAPI كذلك `Idempotency-Key` required header والsuccess/replay responses وأخطاء T17 المتوقعة مثل `KeyRequired`, `KeyReuseMismatch`, `RequestInProgress`, `KeyExpired` و`OutcomeUnknown`، مع `Retry-After` حيث تنص T17.

## 44.1 Business OperationId Transport

لا نخلط بين:

```text
OpenAPI operationId
→ stable unique name of an endpoint in the OpenAPI document

Business operationId
→ UUID identifying one durable business operation under T17
```

عندما `OperationIdSource = CallerSupplied`، تستقبل mutation field باسم `operationId` داخل Explicit Request DTO؛ تكون Canonical non-empty UUID وتدخل validation/fingerprint. لا نستخدم Header عامة باسم `Operation-Id` في Core V1. الـClient تولدها قبل Step-up/Ticket issuance وتحافظ عليها مع نفس `Idempotency-Key` لكل retry لنفس intent.

لو العملية تحتاج `OperationAuthorizationTicket`، Ticket-issuance request والtarget mutation تحملان نفس `operationId`، وتطبق T13–T17 binding rules؛ تغييرها يعني intent مختلفة ويفشل Fail Closed.

عندما `OperationIdSource = ServerGeneratedAtClaim` لا يرسلها Client؛ السيرفر يولدها حسب T17 ويعيدها في success/result DTO، وأي replay تعيد نفس القيمة.

---

# 45. Security-sensitive Authentication Endpoints

Generic HTTP idempotency لا تطبق على:

```text
Login
Refresh
OTP verification
VerificationProof consumption
Recovery code consumption
StepUpGrant consumption
OperationAuthorizationTicket issuance
```

إلا لو القرار الأمني المتخصص قرر semantics مختلفة.

T12/T13/T17 هي المرجع.

---

# 46. OpenAPI

كل Endpoint لازم تظهر Metadata مفيدة.

تشمل:

```text
OpenAPI operationId
Summary
Description

Request contract
Response contracts

Possible status codes

Authentication requirements

Idempotency requirement when applicable
```

`OpenAPI operationId` تكون unique وثابتة داخل document وتتبع naming convention:

```text
<Resource>_<Action>

Examples:
Groups_List
Groups_Create
Enrollments_Withdraw
```

لا تستخدم Module name، ولا تساوي Business `operationId` الموجودة في Request DTO. Build test تولد OpenAPI وتفشل عند duplicate/missing operationId، security classification ناقصة، Request/Response schema مفقودة، أو Required idempotency header غير موثقة.

---

# 47. OpenAPI لا تكشف Internals

Descriptions لا تحتوي:

```text
Database topology
Secrets
Internal security assumptions
Sensitive implementation detail
```

توثق الـClient contract فقط.

---

# 48. Endpoint Security Classification

كل Endpoint تُصنف صراحة على الأقل إلى:

```text
AnonymousSecurity
PublicRead
AuthenticatedUser
InstitutionScoped
PlatformScoped
```

كل Endpoint تختار Classification واحدة primary مع metadata إضافية للassurance/idempotency عند الحاجة. عدم وجود classification يفشل Architecture/OpenAPI test؛ لا يعني AllowAnonymous تلقائيًا.

```text
AnonymousSecurity
→ /api/v1/auth/...
→ explicit AllowAnonymous + security-specific policy

PublicRead
→ /api/v1/public/...
→ explicit AllowAnonymous + visibility/rate-limit policy

AuthenticatedUser / InstitutionScoped / PlatformScoped
→ protected by default
```

وجود Endpoint تحت `/auth` لا يجعلها Anonymous تلقائيًا؛ Step-up/Ticket operations المحمية تظل `AuthenticatedUser` أو classification الأضيق المناسبة مع `SecuritySpecific` idempotency semantics.

---

# 49. Institution Scoped Metadata

Institution-scoped لا تعني Role بعينها.

الـEndpoint/Authorization layer تحدد حسب T14:

```text
Allowed Access Relationship
Permission when Staff
Resource ownership
Branch Scope
Eligibility
Conflict Rules
Approval Requirement
Authentication Assurance
```

T31 لا تكرر الـauthorization matrix.

---

# 50. PublicRead by Explicit Decision Only

Endpoint لا تصبح `PublicRead` لمجرد عدم إضافة Authorization metadata بالخطأ.

PublicRead exposure لازم تكون:

```text
Explicit
Reviewable
Tested
```

والـdefault للBusiness API:

```text
Protected unless explicitly classified as PublicRead or AnonymousSecurity
```

---

# 51. Minimal API Route Groups

طبقًا لـT05، Endpoint definitions تنظم داخليًا باستخدام:

```text
Route Groups per Module / Feature
```

لكن Internal grouping لا يظهر أسماء Modules في Public URL.

مثال داخلي:

```text
Academic module maps group endpoints
```

لكن الـURL:

```text
/institutions/{institutionId}/groups
```

---

# 52. Endpoint Responsibility

Endpoint مسؤولة عن HTTP فقط:

```text
Route / Query / Header extraction
Authentication/Authorization integration
Request mapping
Handler invocation
Result → HTTP mapping
OpenAPI metadata
```

ولا تحتوي Business logic.

---

# 53. Handler لا تعرف HTTP

Handler لا تستقبل:

```text
HttpContext
ClaimsPrincipal
IResult
HttpRequest
HttpResponse
```

وتعيد:

```text
Result
Result<T>
```

طبقًا لـT05/T32.

---

# 54. Cancellation

HTTP Request cancellation تنتقل كـ:

```text
CancellationToken
```

خلال async operations.

لكن cancellation:

```text
≠ proof of rollback
≠ proof operation did not commit
```

خصوصًا للmutation الحساسة طبقًا لـT15/T17.

---

# 55. Security and IDs

API لا تعتمد على:

```text
UUID unpredictability
Hidden URL
Frontend route hiding
```

كـAuthorization.

كل Resource lookup sensitive تمر بـObject-level authorization وTenant isolation.

---

# 56. Cross-Tenant Resource Behavior

Cross-tenant/no-access resource قد ترجع:

```text
404
```

حسب T09/T14 anti-enumeration policy.

Authenticated caller داخل valid context لكنه ناقص Permission:

```text
403
```

التفاصيل النهائية في T32.

---

# 57. API and Module Boundaries

HTTP API ليست طريقة Module-to-Module communication داخل نفس Monolith.

ممنوع:

```text
Module A
→ HttpClient
→ localhost
→ Module B endpoint
```

للتواصل الداخلي الطبيعي.

نستخدم T18 typed in-process contracts.

---

# 58. API and Transactions

Endpoint لا تبدأ Database Transaction.

الـTop-level owning Handler تدير Local Transaction عند الحاجة طبقًا لـT15 كـdefault.

`RecordCashPayment` هي الاستثناء الوحيد: Endpoint تستدعي StudentFinance `RecordCashPaymentOrchestrator`، وهي التي تدير الـnamed atomic scope وparticipants المعتمدة في T15–T18. Endpoint لا تفتح الـscope ولا تحقن DbContexts/transaction handles.

---

# 59. API and Concurrency

HTTP contract يمكن أن تعرض:

```text
409 Conflict
Response DTO: version
Mutation Request DTO: expectedVersion
```

عندما Feature تحتاج ذلك.

لكن concurrency mechanism نفسها T16.

لا يوجد Global ETag requirement في V1.

الـClient لا ترسل `nextVersion`. لو Feature لا تعرض Version للClient فلا تضيف `expectedVersion` شكليًا. Mapping stale/conflict النهائي يتبع T16/T32.

---

# 60. API and Validation

HTTP parsing/basic shape:

```text
Endpoint / Request Validation
```

وتشمل مثلًا malformed UUID/date/enum، missing required field/header، unsupported media type، pagination bounds، unknown filter/sort/query field، وmultiple scalar values.

Use-case preconditions:

```text
Application
```

Business invariants:

```text
Domain
```

والتفصيل النهائي في T32.

---

# 61. API and Background Work

لو request تنشئ Job طويلة:

```text
POST
↓
create durable Job
↓
202 Accepted
↓
Location: /api/v1/.../jobs/{jobId}
↓
return JobId + OperationId + status resource reference
```

عند ملاءمة الـUse Case.

`202` لا تعني إن الـJob نجحت؛ status resource تعرض queued/running/succeeded/failed وفق T21. إنشاء Job ذات duplicate risk المادي يكون Required Idempotency ويعيد نفس JobId/OperationId في replay طبقًا لـT17.

T21 تحسم background execution.

## 61.1 Cache-Control Baseline

في Core V1، قبل قرار T29 المتخصص:

```text
AnonymousSecurity responses
AuthenticatedUser responses
InstitutionScoped responses
PlatformScoped responses
Responses containing credentials, tickets, PII or financial data
→ Cache-Control: no-store

PublicRead
→ no-store by default until an explicit cache policy is approved
```

أي caching لاحقة تكون per endpoint مع `Cache-Control`/`Vary` صحيحة ولا تعتمد على browser defaults. Presigned/file responses تتبع T23. Authentication responses الحساسة تظل `no-store` حسب T13.

---

# 62. No Artificial Async

لا نحول operation سريعة إلى:

```text
202 + background job
```

لمجرد استخدام infrastructure متقدمة.

---

# 63. API Contract Stability

Breaking changes تشمل مثلًا:

```text
Removing/renaming response field
Adding a required request field
Changing optional request field to required
Changing field meaning
Removing/renaming stable enum/code value
Changing stable error code semantics
Changing identifier interpretation
Changing route semantics
```

وتحتاج versioning/migration plan حسب impact.

---

# 64. Additive Changes

إضافة optional response field غالبًا لا تحتاج API major version جديدة طالما الـclient contract يسمح بذلك.

لكن يجب ألا نعتمد على clients تتجاهل أي تغيير بدون تقييم.

---

# 65. Dates and Times

طبقًا لـT11:

```text
Instant
→ ISO 8601 DateTimeOffset

Business Date
→ date representation

Local Time
→ explicit local-time representation
```

ولا نرسل ambiguous server-local timestamps.

Instant output تطبع UTC باستخدام `Z`. Input يمكن أن تقبل `Z` أو explicit numeric offset ثم تحول إلى UTC؛ Instant بلا offset/zone ترفض. قواعد `DateOnly`/`TimeOnly` وIANA TimeZoneId تظل كما في T11.

---

# 66. Money

Money في API تكون explicit ولا تستخدم binary floating point:

```json
{
  "amount": "1250.50",
  "currency": "EGP"
}
```

القواعد:

- `amount` canonical invariant decimal string، بدون grouping separators أو exponent notation.
- `currency` stable uppercase ISO 4217 code.
- الـFeature تحدد allowed scale/range/currencies وتعمل validation قبل mapping إلى Domain Money/decimal.
- لا نرسل amount منفردة أو نفترض Currency من Culture/Timezone/Institution settings داخل نفس Contract.
- Financial IDs/status ليست جزءًا ضمنيًا من Money DTO؛ توثقها Response الخاصة بالFeature.

---

# 67. Boolean Ambiguity

نتجنب Fields غامضة مثل:

```text
active
enabled
valid
```

لو في الحقيقة لدينا أكثر من state.

نستخدم Domain status واضح عندما lifecycle متعددة الحالات.

---

# 68. Bulk APIs

كل Bulk Endpoint تعلن semantics بوضوح:

```text
Atomic
or
Partial
```

طبقًا لـT15/T32.

ولا نفترض أن Bulk يعني Partial تلقائيًا.

كل Bulk contract تكون bounded وتعلن maximum item count. في `Atomic` يكون request كلها intent واحدة ولها Idempotency/OperationId واحدة عند الحاجة. في `Partial` يحمل كل item `clientItemId` فريدة داخل request وتعود نتيجة مستقلة `{ clientItemId, status, code, resourceId? }`؛ لا نعتمد ترتيب array وحده لربط النتائج. أحجام أكبر تتحول Import/Job بدل رفع limit بلا حد.

---

# 69. Import APIs

Imports تستخدم Feature-owned endpoints.

مثل:

```http
POST /api/v1/institutions/{institutionId}/imports/student-records
```

لكن Exact import route تحددها Imports feature.

الـAPI لا تسمح بتجاوز:

```text
Staging
Validation
Preview
Confirmation
Owner-module execution
```

المعتمدة في Business 15.

## 69.1 API Contract and Architecture Tests

الـbuild/test pipeline تثبت على الأقل:

```text
Every route starts with /api/v1
Static segments are lowercase kebab-case
Routes + HTTP methods are unique
OpenAPI operationId values are present and unique
Every endpoint has one primary security classification
Required-idempotency endpoints expose the required header/metadata
AnonymousSecurity/PublicRead are explicitly AllowAnonymous
Protected classifications are not accidentally anonymous
201 responses include Location
202 responses include a status-resource reference
204 responses contain no body
Problem responses use application/problem+json
Sensitive classifications emit Cache-Control: no-store
```

ونستخدم serialization/contract tests للـProblemDetails، Page/Cursor envelopes، UUID/date/time/Money representations، unknown mutation fields، enum codes، وCaller-supplied `operationId`. Critical routes لها integration tests تثبت tenant/object authorization ولا تكتفي OpenAPI metadata.

---

# 70. قواعد ممنوع تتكسر

1. كل Business/Public application HTTP API تحت `/api/v1`؛ operational health/metrics ليست Business API.
2. URLs تعبر عن Business resources لا أسماء Modules.
3. Static route segments lowercase kebab-case، وJSON/query names camelCase.
4. Resource names plural افتراضيًا، و`/me` singleton alias معتمدة للحساب الحالي.
5. Internal UUID هي default identifier للoperational APIs وتخرج lowercase canonical D format.
6. DisplayCode لا تصبح authoritative identifier افتراضيًا.
7. SystemCode/PublicSlug تستخدم فقط بقرار Feature واضح.
8. GET/HEAD لا تطلب Business mutation.
9. Natural creation تستخدم POST collection وتعيد `201 + Location`.
10. Domain state transitions تستخدم explicit POST action routes عند ملاءمتها.
11. PUT complete replacement، وPATCH explicit partial DTO؛ لا Generic JSON Patch في V1.
12. لا Action Routes لكل CRUD بلا داعٍ.
13. InstitutionId من URL Candidate غير موثوقة.
14. Institution Context تُبنى Server-side.
15. InstitutionScoped لا تعني StaffMembership فقط.
16. `AnonymousSecurity` منفصلة عن `PublicRead`، والـPublicRead لها dedicated contracts تحت `/public`.
17. كل Endpoint لها primary security classification صريحة؛ missing classification تفشل الاختبار.
18. Pagination إلزامية للunbounded lists وبحد عام أقصى `100`.
19. Page وCursor contracts منفصلتان وبresponse shapes ثابتة.
20. Cursor opaque/versioned/integrity-protected ومربوطة بالscope والquery، وترتيبها deterministic.
21. Filtering/Sorting allowlisted؛ unknown fields/directions تفشل Validation.
22. لا arbitrary Dynamic LINQ/Reflection/SQL fragments من Client.
23. HTTP DTO منفصلة عن T18 Module Contract وعن Domain/EF types.
24. Normal contract UTF-8 camelCase JSON، والEnums stable strings وليست ordinals.
25. Unknown mutation fields وunknown enum codes تفشل Validation.
26. File transport exceptions صريحة وتتبع T23/T35.
27. Errors تستخدم RFC 9457 و`application/problem+json`.
28. `type/status/title/detail/instance` تتبع RFC semantics، و`code` هي stable EduCenterOS machine contract.
29. `correlationId` تطابق response header، و`traceId` مفهوم مستقل.
30. Correlation/Trace IDs لا تستخدم كSecurity أوBusiness inputs.
31. Idempotency تستخدم Modes وMetadata المثبتة حرفيًا في T17.
32. Required endpoints تستقبل Exactly one canonical UUID `Idempotency-Key`.
33. Caller-supplied Business `operationId` تكون Request DTO field؛ لا تخلط مع OpenAPI operationId.
34. Authentication/Recovery/Step-up/Ticket flows تستخدم `SecuritySpecific` ولا تدخل generic T17 replay.
35. OpenAPI operationId فريدة وثابتة، وكل request/response/status/security/idempotency metadata موثقة.
36. Public exposure وAllowAnonymous صريحة وليست accidental.
37. Endpoint لا تحتوي Business logic، وHandler لا تعرف HttpContext/IResult.
38. API ليست internal module transport؛ التواصل الداخلي يتبع T18.
39. Endpoint لا تبدأ transaction؛ Local transaction default في Handler، وRecordCashPayment تستخدم orchestrator T15–T18 فقط.
40. Client-visible concurrency تستخدم `version`/`expectedVersion` عند الحاجة؛ لا Global ETag في V1.
41. Cross-tenant authorization لا تعتمد على UUID secrecy.
42. Cancellation لا تعني Rollback أو عدم حدوث Commit.
43. `202` تعيد JobId/OperationId وstatus-resource Location ولا تعني نجاح الـJob.
44. `204` لا تحمل response body.
45. Sensitive/protected responses تستخدم `Cache-Control: no-store` في Core V1 حتى قرار T29.
46. Instants/Date/Time تتبع T11، وInstant output تكون UTC `Z`.
47. Money تستخدم canonical decimal string + uppercase ISO 4217 currency، وليس floating point.
48. Bulk semantics معلنة Atomic أو Partial، والrequest bounded والpartial items لها clientItemId.
49. OpenAPI/Architecture/serialization tests جزء من Definition of Done لكل Endpoint.
50. Error/status mapping التفصيلي يظل T32 ولا يغير العقود العامة المثبتة هنا.

---

# خارج نطاق T31

T31 لا تحسم:

```text
Roles / Permissions / Authorization Evaluation
→ T14

Transaction implementation
→ T15

Concurrency mechanisms
→ T16

Idempotency persistence/replay
→ T17

Internal Module Communication
→ T18

Events / Outbox
→ T19

File storage/security implementation
→ T23 / T35

Per-endpoint caching beyond the Core V1 no-store baseline
→ T29

Validation / Error Classification / HTTP Mapping
→ T32

Logging / Correlation implementation
→ T34

Security controls
→ T35
```

---

# القرار النهائي المختصر

> EduCenterOS تستخدم RESTful HTTP API تحت `/api/v1`، بlowercase kebab-case Resource-oriented URLs لا تكشف أسماء الـModules. تستخدم Internal UUIDs كidentifier افتراضي للـoperational APIs، مع السماح بـSystemCode/PublicSlug فقط عندما تفرض الـFeature ذلك صراحة، وتستخدم `/me` و`/me/institutions` للحساب الحالي.

> GET/HEAD لها safe read semantics، والResource creation الطبيعية تستخدم POST للcollection مع `201 + Location`، بينما Domain state transitions الواضحة تستخدم POST Business Action Routes. PUT للcomplete replacement وPATCH لـexplicit partial DTO فقط؛ لا Generic JSON Patch في Core V1. Job غير المكتملة تعاد بـ`202` مع status resource، و`204` لا تحمل body.

> Institution-scoped URLs تحمل `institutionId` كRequested Candidate غير موثوقة؛ الـBackend تبني `IInstitutionContext` Server-side وفق T09/T14، والوصول المؤسسي قد يكون Staff أو Student أو Guardian أو PlatformSupport حسب الـUse Case.

> القوائم تستخدم Page أو integrity-protected Cursor contracts محددة وبحد أقصى عام `100`، مع deterministic ordering وallowlisted filtering/sorting. الـnormal contracts UTF-8 camelCase JSON، والـHTTP DTO لا يعاد استخدامها كModule/Domain contract. Errors تستخدم RFC 9457 `application/problem+json`، مع `code` مستقرة و`correlationId` منفصلة عن `traceId`.

> كل Endpoint تعلن Security classification من `AnonymousSecurity`, `PublicRead`, `AuthenticatedUser`, `InstitutionScoped`, أو `PlatformScoped`. Authentication flows ليست Public data، وprotected/sensitive responses تستخدم `Cache-Control: no-store` في Core V1. OpenAPI operationId فريدة ومختلفة عن Business operationId، والـIdempotency modes والـmetadata والـUUID header تتبع T17 حرفيًا.

> Endpoint لا تبدأ Transaction ولا تنظم Business logic. الـLocal transaction تظل default في الـowning Handler، بينما `RecordCashPayment` وحدها تستدعي الـnamed StudentFinance orchestrator المعتمدة في T15–T18. كل هذه العقود تثبت بـOpenAPI وArchitecture وserialization وintegration tests قبل اعتبار Endpoint مكتملة.
