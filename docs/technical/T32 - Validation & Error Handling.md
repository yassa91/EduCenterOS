# T32 - Validation & Error Handling

## الهدف من القرار

تحديد القواعد الموحدة للتعامل مع Validation وErrors داخل EduCenterOS بحيث:

- يكون مكان كل نوع Validation واضحًا.
- يكون شكل الأخطاء ثابتًا بين الـModules.
- يكون الـHTTP contract متوافقًا مع T31.
- يتم الفصل بين Validation وBusiness Rules وConflicts وSecurity Failures.
- يتم فصل Expected Errors عن Infrastructure/Unexpected Failures.
- لا يتم كشف تفاصيل حساسة للـClient.
- تظل الـApplication والـDomain مستقلتين عن HTTP.
- تكون Error Codes عقودًا ثابتة يمكن للFrontend الاعتماد عليها.
- لا تتحول Exceptions إلى وسيلة عادية للتحكم في Business Flow.

---

# القرار النهائي

يعتمد EduCenterOS:

```text
Layered Validation
+
Strong Error Object Model
+
Small Common Error Categories
+
Module-Owned Stable Error Codes
+
RFC 9457 Problem Details
+
Result / Result<T> for expected failures
+
No Business Exceptions
+
Central Result → HTTP Mapping
+
Sanitized Exception Handling
+
Hybrid Validation Strategy
+
Security-aware Error Disclosure
```

---

# 1. المبدأ الأساسي

نفرق بين أربعة أشياء مختلفة:

```text
Invalid Request
≠
Business Rejection
≠
State Conflict
≠
Infrastructure Failure
```

ولا نضعهم جميعًا تحت:

```text
"Something went wrong"
```

أو:

```text
400 Bad Request
```

---

# 2. Validation Layers

كل Layer مسؤولة عن نوع Validation الخاص بها.

```text
HTTP / Endpoint
↓
Application / Handler
↓
Domain
↓
Database
```

كل طبقة تضيف حماية مناسبة ولا تلغي الطبقات الأخرى.

---

# 3. HTTP / Endpoint Validation

الـEndpoint مسؤولة عن HTTP contract فقط.

مثل:

```text
Malformed JSON
Route parameter parsing
Query parameter parsing
Missing required header
Unsupported request shape
Content-Type requirements
Basic DTO binding
Unknown mutation field / query parameter
Duplicate scalar query/header value
Invalid enum/code representation
```

الأخطاء هنا عادة:

```text
400 Bad Request
```

أو status transport مناسب مثل:

```text
413 Payload Too Large
415 Unsupported Media Type
406 Not Acceptable
```

عند انطباقه.

تطبق قواعد T31 الخاصة بـcase-sensitive camelCase ورفض unknown input fields وmultiple scalar values. Binding failure لا تسرب أسماء CLR types أو نص serializer exception أو القيم المقدمة؛ تتحول لعقد آمن موحد.

أخطاء HTTP مثل `405` و`406` و`413` و`415` تظل Transport failures داخل API layer؛ لا نضيف لها Domain categories ولا نحولها كلها إلى `Validation.Failed`.

---

# 4. Request Validation

الـRequest validation تتحقق من قواعد يمكن تقييمها بدون تحميل authoritative business state.

مثل:

```text
Required field
String length
Format
Numeric range
Allowed enum value
Basic date format
Cross-field shape rule
```

مثال:

```text
StartDate must be before EndDate
```

إذا كانت القاعدة مجرد صلاحية request نفسها وليست Business state متغيرة.

---

# 5. Application / Handler Validation

الـApplication layer تنظم شروط الـUse Case التي تحتاج orchestration أو reads.

مثل:

```text
Required referenced resource exists
Required cross-module fact is available
Use-case preconditions
Cross-field/context checks
```

لكن:

> وجود check في Handler لا يحدد وحده HTTP status.

الـError semantics هي التي تحدد Category والـHTTP mapping.

---

# 6. Domain Validation

الـDomain تحمي Business Invariants التي تملكها.

مثال بعد تحديث Seat ownership:

```text
Enrollments:
New EnrollmentCapacity
cannot be lower than authoritative OccupiedCapacity
```

وليس:

```text
Academic.Group.CapacityExceeded
```

لأن Enrollment Capacity أصبحت مملوكة لـ`Enrollments`.

---

# 7. Database Validation / Constraints

الـDatabase هي آخر خط دفاع للـinvariants التي يمكن تمثيلها فيها.

مثل:

```text
Unique constraints
Check constraints
Foreign keys
Tenant-aware composite constraints
Concurrency predicates
```

Database constraint لا تستبدل Domain/Application validation، والعكس صحيح.

---

# 8. Error Object Model

Expected failures تستخدم Error Object قوي بدل Strings.

الشكل المفاهيمي:

```csharp
public sealed record Error(
    string Code,
    ErrorCategory Category,
    string Description,
    IReadOnlyDictionary<string, ErrorMetadataValue>? Metadata = null,
    IReadOnlyList<ValidationIssue>? ValidationIssues = null,
    bool ValidationIssuesTruncated = false);

public sealed record ValidationIssue(
    string MemberPath,
    string Code,
    string Description);
```

`ErrorMetadataValue` abstraction صغيرة تمثل فقط القيم الآمنة المحددة في القسم 37. الشكل البرمجي التفصيلي يمكن أن يختلف، لكن semantics التالية ثابتة:

- Failure تحمل primary `Error` واحدة؛ لا نجمع Categories مختلفة ثم نخمن status.
- `Code` و`Category` إلزاميتان، و`Description` نص Client-safe by construction.
- `ValidationIssues` تسمح بجمع أخطاء الحقول تحت Category `Validation` فقط؛ كل issue لها stable code ووصف آمن.
- `ValidationIssuesTruncated` تحفظ قرار bounded aggregation حتى تترجمه API إلى `errorsTruncated`. لا تكون true دون non-empty validation issues، ولا تستخدم مع Category أخرى.
- `MemberPath` داخل Application تمثل command/query member مثل `studentId` أو `items[0].name`؛ Domain/Application لا تعرف `body` أو HTTP headers.
- API adapter تترجم Application member paths إلى public input locations وفق mapping صريحة خاصة بالEndpoint. الحقول التقنية مثل Idempotency-Key تتحقق منها API layer مباشرة.
- الـCollections immutable/read-only؛ لا تستخدم Error لتخزين EF objects أو Exceptions أو diagnostics.

المهم أن يحتوي على الأقل:

```text
Code
Category
Description
```

---

# 9. Error Code vs Description

الـClient تعتمد على:

```text
Error.Code
```

وليس:

```text
Description
```

مثال:

```json
{
  "code": "Enrollments.Capacity.BelowOccupiedSeats"
}
```

الـDescription:

```text
Human-facing
Changeable
Localizable later
```

ولا تستخدم كMachine Contract.

---

# 10. Error Categories

الـCategories الأساسية:

```text
Validation
Authentication
Authorization
NotFound
Conflict
BusinessRule
RateLimited
ServiceUnavailable
```

`ServiceUnavailable` Category تقنية للحالات التشغيلية المعروفة التي يصنفها top-level boundary صراحة، ومنها `Idempotency.OutcomeUnknown`. ليست Business rejection ولا تستخدمها Provider contracts لإخفاء outages أو timeouts كـbusiness outcome.

Unexpected failures تستمر كـExceptions حتى boundary ثم تصدر `500 General.UnexpectedError` عبر نفس ProblemDetails writer. لا يلزم تغليف كل Exception في Result أو إضافة Business category مزيفة.

---

# 11. Validation → 400

نستخدم:

```text
400 Bad Request
```

عندما الـrequest نفسها غير صالحة.

مثل:

```text
Missing required field
Invalid format
Invalid range
Malformed route/query value
Cross-field request inconsistency
Missing required Idempotency-Key
Malformed Idempotency-Key
```

أمثلة Codes:

```text
Validation.Failed
Idempotency.KeyRequired
Idempotency.KeyInvalid
```

---

# 12. Authentication → 401

نستخدم:

```text
401 Unauthorized
```

عندما لا يوجد Authentication صالح للعملية.

مثل:

```text
Missing access credential
Invalid access credential
Expired/invalidated session
```

Public authentication flows التي تحتاج anti-enumeration تظل ملتزمة بعقود T12/T13.

كل `401` تحمل `WWW-Authenticate` challenge مناسبة للـAuthentication scheme. في protected Bearer APIs تكون الـbaseline:

```http
WWW-Authenticate: Bearer
```

لا نضيف `error_description` أو token/session internals. API authentication handler تصدر Challenge ثم تستخدم ProblemDetails writer الموحدة؛ لا يعتمد ذلك على وصول الطلب إلى business Handler. أي public flow تختار `401` توثق الـchallenge المناسبة لها ضمن عقدها.

---

# 13. Authorization → 403

نستخدم:

```text
403 Forbidden
```

عندما:

```text
Actor authenticated
+
target context/resource may be revealed
+
required permission/access is missing
```

مثال:

```text
Authenticated staff member
inside valid institution context
but lacks StudentFinance.Refund permission
```

نقص MFA enrollment أو recent authentication أو purpose-bound Step-up على مسار مسموح كشفه يصنف `Authorization → 403` بأكواد stable مثل `IdentityAccess.MfaEnrollmentRequired` و`IdentityAccess.StepUpRequired`. Fresh assurance تخص First execution وفق القسم 49.

Access eligibility مثل membership/relationship/scope/subscription entitlement تتبع T14 وتنتج `403` أو hidden `404`. Business eligibility مثل أهلية طالب للتسجيل بعد إثبات وصول الـActor للعملية تتبع `BusinessRule → 422`.

---

# 14. Hidden Resource → 404

طبقًا لـT09/T14:

```text
Cross-tenant
No valid access relationship
Resource existence should be hidden
```

الـdefault عند الحاجة إلى إخفاء الوجود:

```text
404 Not Found
```

بدل 403 لتقليل enumeration.

وده قرار Security semantics، وليس ادعاء أن الـresource غير موجود فعليًا.

Hidden وReal NotFound لنفس lookup endpoint تستخدمان نفس public `type/title/status/code/detail` ونفس safe extensions. لا يظهر code مثل `CrossTenant` أو `ResourceHidden` أو metadata تثبت وجود سجل، وتراعي القراءة وتقليل فروق التوقيت T14/T35 بقدر عملي. الفروق التشخيصية تسجل داخليًا فقط.

---

# 15. Real Not Found → 404

Resource غير موجود داخل السياق الذي يسمح بالبحث عنه:

```text
404 Not Found
```

ويستخدم Module-specific code متى كانت semantics مهمة.

مثل:

```text
Academic.Group.NotFound
Enrollments.Enrollment.NotFound
```

---

# 16. Conflict → 409

`409 Conflict` تستخدم عندما الـrequest صحيحة شكليًا ومعناها مفهوم، لكن تنفيذها يتعارض مع **current server state** أو operation identity.

أمثلة:

```text
Optimistic concurrency stale version
Duplicate unique business identity
Idempotency key reuse mismatch
Idempotency request still processing
Expired idempotency guarantee
Concurrent state transition conflict
Already-consumed single-use operation where contract treats it as conflict
```

---

# 17. Concurrency Conflicts

T16 هي المرجع.

مثال:

```text
Expected Version = 5
Current Version = 6
```

يمكن أن ينتج:

```text
409 Conflict
```

بـCode مثل:

```text
Common.ConcurrencyConflict
```

أو Module-specific code إذا الـsemantics تحتاج ذلك.

لكن:

> لا نحول كل `DbUpdateConcurrencyException` تلقائيًا إلى 409 بدون فهم الـUse Case.

قد تكون بعض الحالات:

```text
Semantic success
Security failure
Infrastructure defect
```

حسب T16.

---

# 18. Complete Idempotency Error Contract

طبقًا لـT17:

```text
Idempotency.KeyReuseMismatch
→ 409

Idempotency.OperationIdReuseMismatch
→ 409

Idempotency.RequestInProgress
→ 409
→ Retry-After: 1

Idempotency.KeyExpired
→ 409

Idempotency.OutcomeUnknown
→ 503
→ Retry-After: 1
```

أما:

```text
Idempotency.KeyRequired
Idempotency.KeyInvalid
```

فتكون:

```text
400
```

`OperationIdReuseMismatch` تخص same Trusted Scope ونية مختلفة وفق T17، ولا تستخدم لتجاوز replay semantics للنية نفسها. `OutcomeUnknown` تعني أن نتيجة Commit لم يمكن حسمها من authoritative primary؛ لا تعني أن العملية فشلت أو rollback حدثت.

`Retry-After` في Core V1 عدد صحيح موجب من الثواني؛ الـbaseline ثانية واحدة لحالات Idempotency المذكورة، ويمكن ضبطه مركزيًا. Client تعيد نفس intent وKey وOperationId بعد الانتظار؛ الـheader لا تضمن اكتمال العملية خلالها ولا تسمح بإنشاء نية جديدة. لا نضيف Retry-After إلى mismatch أو expired guarantee.

---

# 19. Unique Constraint Conflicts

Known unique constraint violation غالبًا:

```text
409 Conflict
```

مثال:

```text
Institutions.CodeAlreadyExists
```

لكن لا نعتمد فقط على pre-check.

المسار:

```text
Friendly pre-check when useful
+
Database unique constraint
+
Known constraint classification
```

---

# 20. BusinessRule → 422

نستخدم:

```text
422 Unprocessable Content
```

عندما:

```text
Request is syntactically valid
Actor is allowed to attempt the operation
Referenced resources are resolved
No concurrency/duplicate conflict is the primary problem
But an explicit business policy/invariant rejects the requested outcome
```

أمثلة محتملة:

```text
Enrollments.Student.NotEligible
StudentFinance.Refund.ExceedsRefundableAmount
TeacherCompensation.Settlement.NotEligible
```

---

# 21. 409 vs 422

القاعدة العملية:

```text
Conflict with current resource/operation state
→ 409

Business policy says requested outcome is not allowed
→ 422
```

مثال:

```text
Two requests edit same Version
→ 409

Student does not satisfy enrollment eligibility rule
→ 422
```

---

# 22. State Transition Errors

مش كل invalid transition لها status واحدة تلقائيًا.

مثال:

```text
Enrollment already withdrawn
```

لو المشكلة إن command تتعارض مع current lifecycle state:

```text
409
```

أما:

```text
Student is not eligible for this enrollment type
```

فدي:

```text
422
```

الـModule تختار Category حسب المعنى الحقيقي، مش حسب مكان الكود.

---

# 23. Rate Limiting → 429

لو T35/edge policy رفضت الطلب بسبب rate limiting:

```text
429 Too Many Requests
```

مع contract آمنة لا تكشف معلومات حساسة.

Exact rate-limit headers والسياسة التشغيلية تتبع قرار Security/Hosting المناسب.

عندما limiter تعرف وقت انتظار صالحًا، API تضيف `Retry-After` كعدد صحيح موجب من الثواني بتقريب المدة لأعلى. لا نخمن مدة بناءً على account state، ولا تكشف headers معلومات تسمح بـenumeration. أسماء أي RateLimit headers إضافية تظل لـT35.

---

# 24. Unexpected Errors → 500

أي failure غير متوقعة:

```text
Bug
Unknown database failure
Corrupted state
Unhandled exception
Unexpected infrastructure failure
```

ترجع في Production:

```text
500 Internal Server Error
```

بـsanitized response.

مثل:

```json
{
  "type": "urn:educenteros:problem:unexpected",
  "title": "Unexpected error",
  "code": "General.UnexpectedError",
  "status": 500,
  "detail": "The request could not be completed.",
  "correlationId": "...",
  "traceId": "..."
}
```

ولا نكشف التفاصيل الداخلية.

---

# 25. Explicit Service Unavailability

Core V1 تصنف الحالات التشغيلية المعروفة التالية صراحة كـtemporary service unavailability:

```text
503 Service Unavailable
```

`ServiceUnavailable → 503` وتظل technical failure منفصلة عن BusinessRule.

| Condition after owning-flow classification | Public contract |
|---|---|
| `55P03` أثناء idempotency claim arbitration، مع rollback لمحاولة الطلب الحالية | `409 Idempotency.RequestInProgress` + `Retry-After: 1` حسب T17 |
| `55P03` على business-state lock، مع rollback مؤكد | `503 Infrastructure.Busy` + `Retry-After: 1` |
| `40P01` / `40001` بعد استنفاد retry المسموحة في T16 أو عدم السماح بها، مع rollback مؤكد | `503 Infrastructure.Busy` + `Retry-After: 1` |
| Known server/DB execution timeout (`57014` / `25P04`) مع rollback مؤكد والـClient ما زالت متصلة | `503 Infrastructure.Timeout` + `Retry-After: 1` |
| Commit outcome غير معروف (`40003` / `08007` / connection loss around Commit) ولم يمكن حسمه وفق T17 | `503 Idempotency.OutcomeUnknown` + `Retry-After: 1` على flow المحمية بـT17 |
| Unclassified DB/network/provider failure أو programming defect | `500 General.UnexpectedError` |

القواعد:

- SQLSTATE وحدها لا تكفي؛ classifier تعرف مرحلة التنفيذ وowning transaction وسبب timeout وحالة rollback/Commit. Unknown outcome تأخذ الأولوية على Busy/Timeout.
- لا تستخدم `409 Common.ConcurrencyConflict` لـlock timeout أو deadlock؛ stale business version تعالج حسب T16.
- `503` لا تنشئ automatic write retry policy. Retry budget/safety في T16 وsame-intent reconciliation في T17 يظلان إلزاميين.
- technical classification تتم في owning orchestration/top-level boundary؛ لا Provider catch-all تحول infrastructure exception إلى Result business عادية، وفق T18.
- الـbaseline لـBusy/Timeout ثانية واحدة، قابلة للضبط مركزيًا. Expected errors لا تحمل raw SQLSTATE أو constraint names في response.
- أي flow ذات mutation ونتيجة مجهولة تتبع reconciliation الخاصة بها في T15/T17؛ لا نعد من status وحدها بأن الأثر لم يحدث.

لكن:

> لا نحول أي Exception أو Database outage تلقائيًا إلى 503 بلا classification واضحة.

الـdefault للunexpected failure يظل 500 sanitized.

## 25.1 Cancellation and Response Boundaries

- `OperationCanceledException` مرتبطة بـ`RequestAborted`/client disconnect لا تصبح Business Error ولا `500` تلقائيًا. نوقف العمل القابل للإلغاء، ولا نحاول كتابة ProblemDetails على connection المغلقة ولا نستخدم `499` كpublic API contract.
- server timeout مع Client متصلة لا يصنف disconnect؛ يستخدم `Infrastructure.Timeout` فقط بعد classification وrollback المؤكد. Cancellation قرب Commit تتبع unknown-outcome rules قبل تقرير retry.
- Cancellation لا تثبت rollback ولا تسمح بإعادة mutation عمياء، وفق T15/T17/T18/T31.
- إذا Response بدأت بالفعل، لا نغير status ولا نكتب ProblemDetails فوق body جزئية؛ نسجل الواقعة ونتعامل مع connection وفق framework behavior. Errors قبل بدء response فقط يمكن إعادة تشكيلها.
- Unclassified cancellation تظل unexpected failure؛ لا نجعل كل `OperationCanceledException` success أو transient timeout تلقائيًا.

---

# 26. Infrastructure Failure لا تصبح Business Error

ممنوع:

```text
Database unavailable
→ Student.NotFound

Provider timeout
→ PermissionDenied

Module provider crashed
→ BusinessRule
```

Infrastructure failure تظل Infrastructure failure.

وده يحافظ على Fail-Closed بدون تزوير semantics.

---

# 27. Problem Details

HTTP Errors تستخدم:

```text
application/problem+json
```

وفق RFC 9457 Problem Details.

الشكل العام:

```json
{
  "type": "urn:educenteros:problem:business-rule",
  "title": "Business rule rejected",
  "status": 422,
  "code": "Enrollments.Capacity.BelowOccupiedSeats",
  "detail": "The requested enrollment capacity violates the current enrollment rule.",
  "instance": "urn:educenteros:problem-instance:opaque-occurrence-id",
  "correlationId": "...",
  "traceId": "..."
}
```

القواعد التنفيذية:

- `type/title/status/code/detail/correlationId` موجودة في كل ProblemDetails ينتجها التطبيق؛ `status` تطابق HTTP response الفعلية.
- `type` من catalog ثابتة في القسم 59، و`title` ثابتة لنفس type. Core V1 تستخدم English default texts؛ localization لاحقًا لا تغير type/code أو شكل العقد.
- `detail` Client-safe occurrence description؛ في `500` نص عام ثابت. Client لا تعتمد على title/detail/description لاتخاذ قرار برمجي.
- `instance` optional؛ لو موجودة تكون `urn:educenteros:problem-instance:<server-generated-opaque-id>` للواقعة، ولا تستخدم request path/query أو معرف طالب/حساب/tenant.
- `traceId` تظهر عند وجود W3C Activity؛ غيابها يؤدي لحذفها، وليس وضع قيمة CorrelationId مكانها.
- `errors` تظهر فقط حسب القسم 28، و`metadata` extension object فقط عند وجود projection آمنة حسب القسم 37؛ لا نضيف empty extensions عامة.
- `correlationId` و`instance` و`traceId` تشخيصية، ولا تستخدم كauthorization proof أو idempotency identity.
- لا success/failure envelope إضافية حول ProblemDetails. Client تتجاهل unknown extension members حسب T31.

---

# 28. Validation Problem Details

Request validation تستخدم Category `Validation` و`Validation.Failed` كـaggregate code افتراضية، وتضيف `errors` عند وجود public field issues:

```json
{
  "type": "urn:educenteros:problem:validation",
  "title": "Validation failed",
  "status": 400,
  "code": "Validation.Failed",
  "detail": "One or more request values are invalid.",
  "correlationId": "...",
  "errors": {
    "body.name": [
      {
        "code": "Validation.Required",
        "description": "Name is required."
      }
    ]
  },
  "errorsTruncated": false
}
```

ولا نضيف `errors` الفارغة لكل أنواع ProblemDetails.

## 28.1 Public Input Locations

`errors` dictionary من public location إلى non-empty array من `{ code, description }`:

```text
body.name
body.items[0].studentId
query.pageSize
route.studentId
header.idempotency-key
```

- JSON/DTO members تستخدم public camelCase names، وليست CLR/EF/Domain paths. Index داخل collection صفرية البداية.
- Header names تستخدم lowercase للـlocation بغض النظر عن casing الـHTTP الفعلية. Query/route names تتبع public contract حرفيًا.
- whole-body failure مثل Malformed JSON تستخدم `body`، وcross-field issue تستخدم أقرب public containing location مثل `body.period` أو `body`.
- الـpath يولد من known contract mapping، ولا ينسخ arbitrary property names أو submitted values من exception message.
- stable field codes الأساسية: `Validation.Required`, `Validation.InvalidFormat`, `Validation.OutOfRange`, `Validation.TooLong`, `Validation.InvalidValue`, `Validation.InconsistentFields`, `Validation.UnknownField`, `Validation.MultipleValues`, `Validation.MalformedJson`.
- code تفصيلية module-owned مسموحة عندما تضيف semantics مستقرة. Description بشرية آمنة؛ Client تستخدم field code وlocation.
- `Idempotency.KeyRequired/KeyInvalid` تظل top-level codes الخاصة بها وفق T17، ويمكنها إضافة issue تحت `header.idempotency-key` دون تغيير status/type.

## 28.2 Collection and Bounds

- Request validation تجمع الأخطاء المستقلة التي يمكن تقييمها بأمان؛ لا تعمل authoritative business reads لجمع قائمة بكل الرفض المحتمل.
- تستخدم duplicate removal بحسب `(location, code)`، ثم ordinal sorting للـlocations وللـcodes داخل كل location؛ لا تعتمد Client على ترتيب JSON object properties.
- الحد الأقصى Core V1 هو `50` field issues في response؛ validators/aggregation تلتزم بحدود request والcollection في T31 ولا تنفذ عملًا غير محدود لمجرد جمع الأخطاء.
- إذا اكتشفت issues إضافية فوق الحد، تحتفظ bounded aggregation بأول 50 distinct issues وفق validator registration/member traversal order ثابتة ثم ترتب public output. تحفظ `ValidationIssuesTruncated = true` حتى يظهر `errorsTruncated: true`؛ وإلا `false`. هذا الـextension يظهر مع `errors` فقط، ولا يشير لعدد سجلات/موارد سرية.
- Parsing failure التي لا تسمح بتقييم الحقول الأخرى ترجع issue واحدة آمنة عند public containing location؛ لا نفترض أن بقية الحقول صالحة.
- لا يستخدم shared Error model هذا العقد لجمع mixed Authentication/Authorization/BusinessRule categories في response واحدة.
- Feature OpenAPI schema تثبت `errors` كdictionary لقوائم objects، وليس built-in dictionary من message strings؛ framework defaults تكيّف لهذا العقد.

---

# 29. CorrelationId وTraceId

نفرق بين:

```text
CorrelationId
→ request/business-operation correlation

TraceId
→ distributed/diagnostic trace identity
```

T31 تشترط:

```text
X-Correlation-Id
```

على Response.

كل ProblemDetails تحمل إلزاميًا:

```text
correlationId
```

بنفس قيمة `X-Correlation-Id` الفعلية، وتولد API قيمة سليمة عند غياب incoming value أو رفضها وفق T31/T34. `traceId` optional عند وجود Activity، ومفهومها مستقل.

كل retry/replay attempt تحصل على diagnostics الحالية وفق T17؛ لا نعيد CorrelationId/traceId قديمة من stored result. هذه المعرفات لا تمنح الوصول إلى Logs عبر API.

تنفيذ Observability النهائي يتبع T34.

---

# 30. No Sensitive Error Details

في Production ممنوع كشف:

```text
Stack trace
Exception.Message
SQL statement
Constraint internals غير اللازمة
Connection string
Provider secret
JWT
OTP
Refresh credential
File path داخلي حساس
Internal network topology
PII غير اللازمة
```

---

# 31. Security-sensitive Errors

Security flows قد تستخدم Error أقل تفصيلًا من Business flow الطبيعي.

مثل:

```text
Login
Recovery
Verification
Cross-tenant lookup
```

الهدف:

```text
No unnecessary account/resource enumeration
```

ولا نعيد:

```text
"Phone exists but password wrong"
```

لو T12/T13 تعتمد public failure موحدة.

---

# 32. Validation لا تعيد Submitted Secrets

Validation response لا تعيد القيم الحساسة التي أرسلها Client.

مثل:

```text
Password
OTP
Refresh credential
Verification proof
Recovery secret
```

حتى لو كانت invalid.

---

# 33. Error Code Convention

Business Error Codes تستخدم Dot Naming.

الـpattern:

```text
Module.Concept.Reason
```

للأخطاء module-wide التي لا تحتاج Concept إضافية يسمح `Module.Reason` مثل `Institutions.CodeAlreadyExists`. الـprefix والـcode case-sensitive وثابتتان؛ لا نستنتج اسم code ديناميكيًا من CLR type أو exception.

أمثلة:

```text
Enrollments.Capacity.BelowOccupiedSeats

Enrollments.Student.NotEligible

StudentFinance.Refund.NotAllowed

Academic.Group.NotFound

Institutions.CodeAlreadyExists
```

---

# 34. Module Names في Error Codes

نستخدم أسماء الـBusiness Modules الحالية:

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

ولا نستخدم أسماء قديمة مثل:

```text
Payments.*
TeacherContracts.*
```

---

# 35. Stable Error Codes

Error Code جزء من الـAPI contract.

تغيير:

```text
StudentFinance.Refund.NotAllowed
```

إلى Code مختلفة بلا migration قد يكون Breaking Change للClient.

لذلك الـCodes:

```text
Named intentionally
Reviewed
Stable
Documented in OpenAPI where useful
```

---

# 36. Error Description

`Description`:

```text
Client-safe human-readable default
```

لكن لا يعتمد عليها Client logic.

يمكن ترجمتها أو تغيير صياغتها بدون تغيير Error Code.

لا تحمل `Description` exception messages أو diagnostics أو raw submitted values؛ الـwriter لا يفترض إمكانية إصلاح نص غير آمن بمجرد serialization. Internal diagnostics تسجل منفصلة، وsecurity disclosure policy قد تستبدل الوصف بنص عام أكثر تحفظًا.

---

# 37. Metadata

Error Metadata تستخدم فقط عندما تضيف قيمة حقيقية.

مثل:

```text
expectedVersion
currentVersion
```

لو قرر الـUse Case كشفهم.

لكن الـHTTP mapper تعمل:

```text
Explicit safe allowlist per public Error.Code
```

ولا تسكب internal metadata تلقائيًا للClient.

القواعد المعتمدة:

- `ErrorMetadataValue` تقبل bounded string / bool / int / long / decimal / Guid / null فقط. IDs وdecimal تserialize حسب T31؛ Money تستخدم feature-owned safe projection بعقد T31 بدل arbitrary object.
- Arbitrary objects وCollections وExceptions وEF entities وSQL/constraint names غير مسموحة داخل generic error metadata.
- كل public code تسجل allowed keys وtypes وmaximum string lengths وvisibility policy؛ unknown key/type تسقط من HTTP projection وتكشفها development/test contract checks.
- metadata تظهر تحت extension اسمها `metadata`؛ لا يسمح بمفاتيح تعيد تعريف type/status/code/errors/correlationId.
- `expectedVersion/currentVersion` لا تظهر إلا بعد current resource visibility وpermission؛ Hidden NotFound وpublic security failures لا تحمل هذه metadata.
- technical Retry-After تحددها API adapter/configuration وtyped boundary classification، ولا تمرر raw headers أو HttpContext عبر Error model.

---

# 38. Common Error Catalog

BuildingBlocks يمكن أن تملك فقط Errors عامة فعلًا.

مثل:

```text
Validation.Failed
General.UnexpectedError
Common.ConcurrencyConflict
Infrastructure.Busy
Infrastructure.Timeout
```

لكن نتجنب استخدام:

```text
Common.NotFound
Common.Conflict
```

لكل شيء إذا كان الـClient يحتاج معرفة Business concept الحقيقي.

Module-specific semantics تظل Module-owned.

أكواد `Idempotency.*` مملوكة للـshared idempotency mechanism، وtransport codes مثل `Http.MethodNotAllowed` مملوكة لـAPI infrastructure. Public error definitions تجمع في registry ثابتة عند composition root، دون نقل business catalogs إلى BuildingBlocks.

---

# 39. Module-owned Error Catalog

كل Module تملك أكواد Business الخاصة بها.

مثال:

```text
Enrollments.Capacity.BelowOccupiedSeats
Enrollments.Student.NotEligible

StudentFinance.Refund.NotAllowed
StudentFinance.Payment.AlreadyReversed
```

BuildingBlocks لا تتحول إلى:

```text
Global Business Error Catalog
```

---

# 40. Result Pattern

Expected failures داخل Application تستخدم:

```text
Result
Result<T>
```

مثل:

```csharp
return Result.Failure(
    EnrollmentErrors.StudentNotEligible);
```

ولا تستخدم Exceptions للتحكم في expected Business flow.

Success لا تحمل Error، وFailure تحمل primary Error واحدة دون success value. `Result<T>.Value` لا تقرأ في failure. Domain/Result factories تمنع الحالات المتناقضة؛ إظهار invalid Result state يعتبر programming defect.

`Result.ValidationFailure(issues)` أو equivalent factory تجمع issues ذات Category Validation وتنتج `Validation.Failed`. Public location projection تتم في Endpoint adapter، ولا نضع HTTP source prefixes في Domain/Application models.

---

# 41. Business Exceptions ممنوعة

مرفوض كـnormal flow:

```csharp
throw new StudentNotEligibleException();
```

ثم Global Exception Handler تحولها لـ422.

المعتمد:

```text
Expected failure
→ Result/Error
```

---

# 42. Exceptions

Exceptions تستخدم في:

```text
Unexpected failures
Infrastructure failures
Impossible/corrupted internal state
Programming errors
```

ويتم التقاطها عند application boundary المناسبة وتسجيلها ثم إرجاع response sanitized.

Known provider exceptions يمكن تحويلها إلى expected Error داخل owning infrastructure/transaction flow إذا السبب معروف وrollback semantics صحيحة، مثل known unique violation. Catch-all داخل Provider ممنوعة وفق T18؛ exception غير المصنفة تصل للـtop-level boundary.

التسجيل التشخيصي للunexpected exception يتم مرة واحدة عند boundary المخصصة، مع request correlation وredaction. لا يسجل كل Layer نفس exception ثم يعيد throw. Classified technical failures والcancellations تسجل حسب T34 دون تزوير Business codes.

---

# 43. Handler لا ترجع HTTP

Handler لا ترجع:

```text
IResult
ProblemDetails
StatusCode
HttpResponse
```

ترجع:

```text
Result
Result<T>
```

الـHTTP mapping تظل في API layer حسب T05/T31.

---

# 44. Central Result → HTTP Mapping

نستخدم Mapping مركزية مثل:

```csharp
result.ToHttpResult(...)
```

أو equivalent abstraction.

وظيفتها:

```text
Error Category
↓
HTTP Status
↓
ProblemDetails
```

بدون نقل HTTP concepts إلى Domain/Application.

Result adapter وAuthentication Challenge/Forbid وtransport/status handlers وexception boundary تستخدم **ProblemDetails writer واحدة** للعقد في الأقسام 27–29 و59. لا نفترض أن Result mapper وحدها تغطي failures التي تحدث قبل Handler.

Unregistered public code أو unknown ErrorCategory لا تنتج guessed 400/422؛ تعتبر configuration/programming defect وتفشل build/startup verification أو ترجع sanitized 500 لو ظهرت أثناء التنفيذ. Registry تربط Result public code بـCategory وmetadata projection؛ transport/exception codes لها fixed API descriptors من catalog دون إدخال transport concepts في ErrorCategory.

Cross-module Result/negative outcome لا تنسخ إلى public Error تلقائيًا. Top-level Use Case تختار public code وdisclosure policy المناسبة وفق T18؛ registry للعقود المنشورة فقط، وليست نافذة على internal module errors.

---

# 45. Central Mapping ليست Global Business Logic

الـmapper تعرف:

```text
Category → HTTP semantics
ProblemDetails formatting
Safe metadata projection
```

لكن لا تقرر:

```text
هل الطالب eligible؟
هل Refund مسموح؟
هل المقعد متاح؟
```

دي مسؤولية الـowning Module.

---

# 46. Validation Library Strategy

نعتمد Hybrid Approach.

```text
Simple validation
→ manual / lightweight checks

Complex request validation
→ FluentValidation
```

حسب الحاجة.

FluentValidation لا تدخل Domain layer.

Request validators تعتمد على deterministic shape checks، ولا تعمل writes أو authoritative eligibility/authorization queries. قواعد state تظل في Application/Domain داخل transaction/protocol المناسب.

عند استخدام FluentValidation داخل Minimal API filter نستدعي `ValidateAsync` مع CancellationToken ونحوّل النتائج إلى `ValidationIssue`؛ لا نعتمد على MVC auto-validation ولا نعرض default FluentValidation error codes أو CLR paths كناتج HTTP. Manual وFluentValidation validators تنتج نفس stable issue contract.

---

# 47. Endpoint Filters

في Minimal APIs، request validation المشتركة يمكن تطبيقها باستخدام:

```text
Endpoint Filters
```

المسار المفاهيمي:

```text
Request
↓
Binding / Parsing
↓
Endpoint Filter / Validator
↓
Handler
```

لكن Business authorization/domain decisions لا تتحول إلى Generic Validation Filter.

تتولى shared filters request-shape validation فقط. Authentication/current context وbase permission/visibility تتبع pipeline الأمنية المعتمدة، بينما handlers/orchestration تراجع resource authorization وbusiness state في موضعها الصحيح. Binding/transport قد ترفض الطلب مبكرًا، لكنها لا تحمل authoritative resource/account information.

## 47.1 Framework and Transport Coverage

- API composition تستخدم ASP.NET Core ProblemDetails services (`AddProblemDetails` أو equivalent) وcentral writer/customization تلتزم بالعقد المخصص.
- Exception handling، binding failures، Authentication Challenge/Forbid، routing `404/405`، request-size `413`، content negotiation `406/415` وrate-limit `429` لها explicit adapters/tests؛ Endpoint filters وحدها لا تغطيها.
- `405` تحتفظ بـ`Allow` التي تعبر عن supported methods، و`401` تحتفظ بـWWW-Authenticate. تغيير body لا يلغي protocol headers.
- Core V1 تختار application/problem+json كصيغة error التشخيصية حتى عندما Accept لا تسمح بصيغة success؛ لا ينتج 406 loop ولا HTML error page.
- `HEAD` ترجع status والheaders المناسبة بدون body. Response-started/client disconnect تتبع القسم 25.1.
- failures التي ينتجها reverse proxy/server قبل وصول الطلب للتطبيق قد تكون خارج writer؛ توثق coverage عند hosting decision ولا يدعي التيم أن application filters تتحكم في edge كلها.

---

# 48. Validation Runs Before Idempotency Reservation

طبقًا لـT17، request validation الأساسية تحدث قبل إنشاء idempotency reservation.

مثال:

```text
Invalid request
→ 400
→ no completed idempotency record
```

عشان Client تقدر تصلح الطلب وتعيده.

المقصود request-shape validation وKey/OperationId validity لبناء fingerprint، وليس first-execution business eligibility أو fresh Step-up. Invalid preflight request لا تنشئ committed reservation/effect.

إذا كانت same key لها Completed outcome بالفعل، تغيير النية الصالحة لا يعد تصحيحًا لمحاولة قديمة؛ يفشل `KeyReuseMismatch`. Reservation والBusiness locks وrollback/replay storage تظل حسب T17.

---

# 49. Authentication / Authorization Before Replay

Idempotency replay لا تتجاوز security.

كل retry تعيد:

```text
Authentication
Institution Context
Authorization
```

ثم يمكن إعادة stored result إذا ما زال الوصول مسموحًا.

الترتيب التنفيذي للـRequired endpoints يلتزم T17:

```text
Current Authentication / validated context / base permission + visibility
↓
Request shape + Key/OperationId validation + trusted fingerprint
↓
Authoritative-primary Completed lookup
├─ Completed: current access check + same-intent checks → semantic replay
└─ No Completed: fresh assurance/ticket when required
   → owning idempotency claim → business rules/mutation → atomic commit
```

Completed replay لا تعيد eligibility/state-transition rules الخاصة بتنفيذ command؛ العملية الأصلية قد غيرت state. ولا تطلب Ticket الأصلية حية أو Fresh Step-up، ولا تعيد استهلاك Ticket. Current permission/visibility revoked تؤدي إلى `403/404` دون كشف stored result.

First execution تعيد full current authorization وfresh OperationAuthorizationTicket عند اشتراطها؛ Completed lookup المفقودة لا تمنح bypass. Replay reconstruction ترجع same safe semantic result مع current response correlation/trace طبقًا T17.

---

# 50. Known Database Constraint Mapping

Known DB constraint failure:

```text
DbUpdateException
↓
identify known constraint
↓
map to stable Error
↓
Result / HTTP contract
```

مثال:

```text
uq_institutions_normalized_code
↓
Institutions.CodeAlreadyExists
↓
409
```

الـmapping تملكها الـModule صاحبة constraint، بتطابق exact constraint identity + SQLSTATE. Unique `23505` وحدها لا تعني نفس business code لكل tables، ولا نضع constraint catalog خاصة بالـModules داخل global exception handler.

Known check/FK violation لا تتحول تلقائيًا إلى `400/422/404`؛ تحتاج feature-approved semantics وauthorization-safe disclosure. Constraint ناتجة عن internal defect أو unknown tenant integrity failure تظل sanitized 500.

بعد DB failure داخل explicit transaction لا نواصل القراءة أو SaveChanges على aborted transaction؛ نعمل rollback/dispose أولًا. أي إعادة قراءة للتصنيف تتم بcontext/transaction جديدة وفق T15/T16، باستثناء savepoint recovery الموثقة صراحة. Idempotency arbitration تستخدم `ON CONFLICT DO NOTHING` حسب T17.

---

# 51. لا Mapping حسب Exception Message

ممنوع:

```text
if exception.Message.Contains("duplicate")
```

كعقد Production.

نستخدم:

```text
Provider error code
Known constraint identity
Typed exception information
```

حسب PostgreSQL/Npgsql implementation.

---

# 52. Unknown Database Failure

لو DB failure غير مصنفة:

```text
Do not invent Business Error
```

تظل:

```text
Infrastructure / Unexpected
→ sanitized 500 by default
```

مع Logs/Observability داخلية.

---

# 53. Bulk Operations

كل Bulk Use Case تعلن في Contract إذا كانت:

```text
Atomic
or
Partial
```

طبقًا لـT15/T31.

---

# 54. Atomic Bulk Error

لو العملية Atomic:

```text
one required item fails
→ whole operation fails
```

وتستخدم ProblemDetails عادية مناسبة للسبب.

---

# 55. Partial Bulk Result

لو الـUse Case مصممة كPartial:

يمكن أن ترجع Response business-specific مثل:

```json
{
  "processed": 2,
  "succeeded": 1,
  "failed": 1,
  "items": [
    {
      "clientItemId": "item-001",
      "status": "Succeeded",
      "code": null,
      "resourceId": "3af63ed2-b97a-4b3b-bc36-1bb7a8d990ef"
    },
    {
      "clientItemId": "item-002",
      "status": "Failed",
      "code": "Imports.Student.DuplicateExternalReference",
      "resourceId": null
    }
  ]
}
```

الشكل الدقيق لكل Feature.

`index` لا تستخدم كcorrelation contract؛ كل input item تحمل `clientItemId` فريدة داخل request وتعود نفس القيمة في نتيجتها وفق T31. Item status ثابتة `Succeeded/Failed` في هذا العقد؛ code إلزامية عند failure وnull عند success البسيطة، وresourceId nullable حسب النتيجة. كل item مقبولة للتنفيذ لها نتيجة، والsummary counts تطابق النتائج.

Request كلها يجب أن تكون valid shape وضمن maximum item count، وduplicates في clientItemId تفشل `400` قبل effects. Top-level Authentication/Authorization/request failure تستخدم ProblemDetails؛ item-level outcomes تستخدم typed business result. Completed synchronous Partial execution ترجع `200` بعقد Feature موثق، ولا تستخدم `207 Multi-Status` كdefault.

ولا نستخدم Partial behavior بدون قرار صريح.

---

# 56. Import Validation Errors

Imports لها error reporting أغنى من HTTP request validation العادية.

مثال:

```text
Row number
Column
Stable code
Safe description
```

وده جزء من Import Report/Staging workflow حسب Business 15.

لا نحاول ضغط كل Import errors في generic `Validation.Failed`.

---

# 57. Error Logging

T32 لا تحدد Logging implementation.

لكن القاعدة:

```text
Client-safe Error
≠
Internal diagnostic record
```

الـLogs/Traces قد تحتاج تفاصيل أكثر داخليًا، مع Redaction حسب T34/T35.

---

# 58. Do Not Log Every 4xx as Server Failure

Expected:

```text
400
404
409
422
```

ليست تلقائيًا Application Exceptions.

Observability policy تحدد level/metrics حسب النوع.

---

# 59. Stable Problem Type Catalog

Core V1 تثبت catalog التالية. `type` تعرف broad problem type وفق RFC 9457، و`code` extension تعرف السبب التفصيلي الذي تستخدمه تطبيقاتنا. لا نولد type URI لكل request أو من Error.Description.

| Source / Category | HTTP | Type suffix after `urn:educenteros:problem:` | Stable English title |
|---|---:|---|---|
| Validation | 400 | `validation` | Validation failed |
| Authentication | 401 | `authentication` | Authentication required |
| Authorization | 403 | `authorization` | Access denied |
| NotFound | 404 | `not-found` | Resource not found |
| Conflict | 409 | `conflict` | Request conflict |
| BusinessRule | 422 | `business-rule` | Business rule rejected |
| RateLimited | 429 | `rate-limited` | Rate limit exceeded |
| ServiceUnavailable | 503 | `service-unavailable` | Service unavailable |
| Unexpected exception fallback | 500 | `unexpected` | Unexpected error |
| HTTP method rejection | 405 | `method-not-allowed` | Method not allowed |
| HTTP representation negotiation | 406 | `not-acceptable` | Representation not acceptable |
| HTTP request size rejection | 413 | `payload-too-large` | Payload too large |
| HTTP request media type rejection | 415 | `unsupported-media-type` | Unsupported media type |

Transport public codes على الترتيب: `Http.MethodNotAllowed`, `Http.NotAcceptable`, `Http.PayloadTooLarge`, `Http.UnsupportedMediaType`. Unmatched route تستخدم `Http.RouteNotFound` مع NotFound type، بينما feature lookup تستخدم module-owned public NotFound code متطابقة للhidden/absent cases في نفس contract.

هذه registry تقنية عامة لا تحتوي business rules. Module-owned catalogs تسجل public codes وCategory وsafe metadata projection، ولا يحتاج mapper إلى معرفة refund أو capacity logic.

Core V1 لا تحتاج documentation server للـtype URIs؛ تسجل معانيها وextensions داخل documentation/OpenAPI. لا نستبدل URNs تلقائيًا بـHTTPS URLs عند إنشاء موقع توثيق لاحقًا؛ تغيير type contract يحتاج compatibility review. لا يستخدم `about:blank` للأخطاء المصنفة في catalog، مع بقاء استخدامه العام مسموحًا في T31.

---

# 60. HTTP Status Summary

| Error Meaning | HTTP |
|---|---:|
| Malformed / Request Validation | 400 |
| Authentication Failure | 401 |
| Authorization Failure | 403 |
| Not Found / Hidden Resource | 404 |
| Method Not Allowed | 405 |
| No Acceptable Representation | 406 |
| Current-State / Concurrency / Duplicate Conflict | 409 |
| Payload Too Large | 413 |
| Unsupported Media Type | 415 |
| Business Rule / Eligibility Rejection | 422 |
| Rate Limited | 429 |
| Unexpected / Unclassified Infrastructure Failure | 500 |
| Classified Busy / Timeout / Unresolved Commit Outcome | 503 |

## 60.1 Required Contract Verification

T33 تحدد تنظيم tests، لكن implementation لـT32 يجب أن تثبت:

```text
Every registered Result error code has exactly one approved Category
Transport/exception codes have fixed API descriptors
Category/transport mapping uses the fixed type/title/status catalog
HTTP status equals ProblemDetails.status
Result + framework + security failures use the same response schema
X-Correlation-Id equals body.correlationId
Validation locations use public names + stable field codes
Multiple issues, duplicate removal, ordering and 50-issue cap
No raw exception/SQL/secret/submitted-value exposure
Metadata projection enforces the allowlist per public code
401 retains WWW-Authenticate; 405 retains Allow
406/413/415/429 have safe transport adapters
Hidden and absent resource have the same public failure contract
Completed replay skips first-execution business rules and fresh tickets
RequestInProgress: 409 + Retry-After; OutcomeUnknown: 503
General lock timeout is Busy 503; never fake stale 409
Client disconnect is not a synthetic 500 response
Partial results correlate by clientItemId
```

Tests التي تثبت rollback/Commit ambiguity/concurrency تظل على PostgreSQL الحقيقية وفق T15–T17. Schema snapshots لا تغني عن verification أن security/framework paths قبل Handler تستخدم نفس writer.

---

# 61. قواعد ممنوع تتكسر

1. كل Layer تعمل Validation الخاصة بها.
2. Request validation لا تتحول إلى Domain logic.
3. Domain تحمي invariants التي تملكها.
4. Error Code هي machine contract.
5. Description ليست machine contract.
6. Business errors تستخدم Result وليس Exceptions.
7. Infrastructure failure لا تتنكر كBusiness Error.
8. Validation = 400.
9. Authentication = 401 مع WWW-Authenticate مناسبة.
10. Authorization = 403 عندما resource/context يمكن كشفهما.
11. Hidden resource = 404 حسب T09/T14، بنفس public contract للabsent resource.
12. Current-state/concurrency/idempotency conflict = 409.
13. Business outcome/eligibility rejection = 422؛ access/assurance eligibility تتبع 403/404.
14. Known unique constraint غالبًا = 409.
15. Unknown DB failure ليست 409 عشوائيًا.
16. Security errors تقلل التفاصيل عند الحاجة.
17. ProblemDetails لا تكشف internals/secrets.
18. Validation responses لا تعيد sensitive submitted values.
19. Module-specific errors تظل داخل Module.
20. BuildingBlocks لا تتحول إلى global business catalog.
21. Handler لا ترجع HTTP response.
22. HTTP mapping مركزي في API layer.
23. FluentValidation لا تدخل Domain.
24. Completed replay تعيد current access checks وتستثني first-execution business rules/fresh tickets.
25. Bulk semantics تكون Atomic أو Partial بقرار صريح.
26. Error metadata لا تظهر للClient إلا عبر typed values وsafe allowlist per public code.
27. Error codes تستخدم أسماء Modules الحالية.
28. Field issues لها public location وstable code وsafe description وبحد أقصى 50.
29. Known Busy/Timeout/OutcomeUnknown تستخدم ServiceUnavailable؛ unexpected failures تظل 500.
30. RequestInProgress تستخدم 409 + Retry-After: 1 وفق T17.
31. ProblemDetails type/title ثابتتان، وcorrelationId إلزامية، وinstance optional opaque URI.
32. Partial Bulk تربط النتائج بـclientItemId، والrequest bounded وفق T31.
33. Response-started/client disconnect لا تنتجان ProblemDetails body ثانية.
34. Framework/transport/security error paths تستخدم نفس writer مع protocol headers المطلوبة.

---

# خارج نطاق T32

T32 لا يحسم:

```text
Authentication implementation
→ T12 / T13

Authorization policies
→ T14

Transaction semantics
→ T15

Concurrency classification details
→ T16

Idempotency semantics
→ T17

API route/contract standards
→ T31

Testing strategy
→ T33

Logging / Metrics / Tracing
→ T34

Security hardening / rate-limit policy
→ T35

Configuration / Secrets
→ T36
```

---

# Definition of Done

يعتبر T32 مقفولًا عندما:

- مكان كل نوع Validation واضح.
- Error/Result invariants وmultiple ValidationIssue model واضحة ومستقلة عن HTTP.
- Error Categories محددة.
- 400 / 401 / 403 / 404 / 409 / 422 / 429 / 500 / 503 semantics وtransport status mapping واضحة.
- Infrastructure failures منفصلة عن Business errors.
- Error Code convention ثابتة.
- Module ownership للأخطاء واضحة.
- ProblemDetails fields وtype/title catalog وpublic validation locations/codes موحدة.
- Validation errors bounded إلى 50 وتعلن truncation عند حدوثها.
- Sensitive error disclosure policy واضحة.
- Exceptions/rollback/Commit ambiguity/cancellation/response-started boundaries واضحة.
- Result → HTTP mapping مركزي.
- Framework/security/transport failures مغطاة بنفس writer مع WWW-Authenticate/Allow.
- Bulk error semantics مرتبطة بـAtomic/Partial contract وclientItemId.
- T16/T17 conflicts متوافقة مع T32.
- RequestInProgress وOperationIdReuseMismatch وOutcomeUnknown مثبتة بالكامل.
- Metadata projection typed وبallowlist لكل public code.
- Contract verification checklist معتمدة لتنفيذها في T33.
- Application/Domain تظل مستقلة عن HTTP.

---

# القرار النهائي المختصر

> EduCenterOS تستخدم Layered Validation وStrong Error Model. Expected failures ترجع `Result / Result<T>` مع stable Error Code وCategory، بينما Exceptions تظل للأخطاء غير المتوقعة أو Infrastructure failures.

> `400` مخصصة لInvalid Request/Validation، و`409` لتعارض الطلب مع current state مثل concurrency وduplicate/idempotency conflicts، بينما `422` لطلب صحيح ومفهوم لكن Business policy أو eligibility rule ترفض نتيجته. `401/403/404` تتبع Authentication/Authorization/anti-enumeration semantics في T13/T14.

> Known lock contention/timeouts بعد rollback مؤكد تستخدم `503 Infrastructure.Busy/Timeout`، بينما commit ambiguity غير المحسومة تستخدم `503 Idempotency.OutcomeUnknown` وفق T17. Idempotency arbitration waiting وحدها تستخدم `409 Idempotency.RequestInProgress` مع Retry-After؛ Unknown failures تظل sanitized 500 ولا تتوسع automatic write retries.

> Error Codes مملوكة للـModule التي تملك الـBusiness concept؛ لذلك Seat Capacity errors أصبحت `Enrollments.*` والمالية `StudentFinance.*`. BuildingBlocks تحتوي فقط errors عامة فعلًا ولا تصبح Global Business Error Catalog.

> الـAPI تستخدم RFC 9457 Problem Details بواسطة writer واحدة وtype/title catalog ثابتة، مع `correlationId` إلزامية مطابقة للresponse header و`traceId` عند وجود Activity وoptional opaque instance. Field errors تحمل public location وstable code وsafe description ضمن حد 50؛ metadata تستخدم typed values وallowlist per code. Sensitive details لا تظهر للClient، وCompleted replay تعيد current access checks دون first-execution rules أو fresh tickets، وPartial Bulk تستخدم clientItemId حسب T31.

---

## المراجع التنفيذية

- [RFC 9457 — Problem Details for HTTP APIs](https://www.rfc-editor.org/rfc/rfc9457.html): تعريف type/status/title/detail/instance وextension semantics. Field-error schema وCategory/code catalog أعلاه اختيارات EduCenterOS وليست shape يفرضها RFC.
- [RFC 9110 — HTTP Semantics](https://www.rfc-editor.org/rfc/rfc9110.html): HTTP status semantics وWWW-Authenticate وAllow وRetry-After.
- [ASP.NET Core API error handling](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling-api): ProblemDetails services وexception/status handling؛ تكييفها للعقد المخصص يجب إثباته على paths الفعلية.
- [FluentValidation asynchronous validation](https://docs.fluentvalidation.net/en/latest/async.html): explicit ValidateAsync عند استخدام validators مع async execution.
