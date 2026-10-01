# T18 - التواصل المتزامن بين الـModules - القرار التقني المعتمد

## الهدف من القرار

تحديد الشكل الموحد للتواصل المتزامن `Synchronous Communication` بين الـBusiness Modules داخل EduCenterOS، بحيث:

- تحافظ كل Module على ملكية بياناتها ومنطقها.
- تستطيع Module طلب Fact أو Validation أو Operation من Module أخرى بدون الوصول إلى `DbContext` أو Entities الخاصة بها.
- تظل حدود الـModular Monolith حقيقية على مستوى الـCompiler والـPersistence، وليست مجرد فولدرات.
- تكون اتجاهات الـProject References واضحة ومقصودة ومن غير Circular Dependencies.
- لا نستخدم HTTP/gRPC/Message Broker للتواصل الداخلي داخل نفس الـProcess بدون حاجة.
- لا ننشئ `God Service`, `Module Bus`, أو Service Locator يخفي الاعتمادات الحقيقية.
- نفرق بين Synchronous Query وSynchronous Command وبين Events/Outbox.
- نمنع Shared Transactions أو Cross-Module Locks كحل عام لعمل Atomicity عبر Modules، مع الحفاظ على استثناء `RecordCashPayment` الوحيد المعتمد والمقيد في T15–T17.
- نحدد شكل الـContracts العامة، وما يسمح أن يخرج من الـModule وما يجب أن يظل `internal`.
- نحدد Error/Failure semantics عند حدود الـModules بدون تسريب تفاصيل EF Core/PostgreSQL أو HTTP.
- نحدد قواعد Cancellation وAsync execution وDI lifetimes.
- نمنع N+1 Cross-Module Calls والاعتماد على Lazy data خارج الـModule.
- نحمي Multi-Tenancy وAuthorization وPrivacy عند تبادل البيانات.
- نحسم Contract المطلوبة مباشرة في Sprint 03 بين `Institutions` و`IdentityAccess` لقراءة Account Facts.
- نترك Events/Outbox وBackground processing وCaching وObservability لقراراتها المتخصصة بدون تعارض.

---

# القرار النهائي

يعتمد EduCenterOS:

```text
In-process typed synchronous contracts
+
Provider Module owns the public Contract
+
Consumer → Provider one-way Project Reference
+
Contracts live inside the Provider Module project under Contracts/
+
Internal-by-default implementation and domain types
+
Direct constructor injection of narrow typed interfaces
+
Queries/Facts are the preferred synchronous cross-module interaction
+
Ordinary synchronous cross-module commands are exceptional and never imply shared atomicity
+
No direct foreign DbContext / Entity / Repository access
+
No cross-module IQueryable / DbSet / tracked objects
+
No internal HTTP/gRPC calls between Modules in the same host
+
No generic Module Bus / Service Locator / reflection-based dispatch
+
No circular Module references
+
No unapproved shared cross-module database transaction or TransactionScope

One named atomic-participant exception only:
RecordCashPayment under the exact T15–T17 protocol
+
No cross-module call while holding a local DB transaction by default
+
Provider returns minimal immutable contract data only
+
Consumer owns its own Business decision using provider-owned facts
+
Current Authentication / Tenant / Authorization remains server-side and fail-closed
+
CancellationToken flows through every I/O contract
+
No N+1 module calls; define bounded bulk contracts when genuinely required
+
Infrastructure failures remain infrastructure failures and are not disguised as NotFound/Denied
+
Real integration + architecture tests prove the module boundary
```

الصورة العامة:

```text
HTTP Endpoint
↓
Top-level Handler in Consumer Module
↓
Consumer's local validation / current actor / institution context
↓
Typed Contract owned by Provider Module
↓
Provider internal implementation
↓
Provider DbContext / Domain / local rules only
↓
Minimal immutable Fact / Result
↓
Consumer interprets the Fact for its own Use Case
↓
Consumer local transaction if needed
↓
Consumer SaveChanges / Commit
↓
Result → HTTP via T32
```

مثال Sprint 03:

```text
Institutions
↓ Project Reference
IdentityAccess.Contracts
↓
IAccountFactsReader
↓
IdentityAccess internal implementation
↓
IdentityAccessDbContext
↓
AccountFacts
↓
Institutions decides eligibility / context behavior
```

ولا يحدث:

```text
InstitutionsDbContext
→ IdentityAccess tables ❌

Institutions
→ IdentityAccess Entity ❌

IdentityAccess
→ Institutions callback/reference ❌
```

---

# 1. العلاقة مع القرارات السابقة

T18 تبني على:

```text
T01
→ .NET 10 / ASP.NET Core 10 / EF Core 10
→ In-process dependency injection متاحة مباشرة

T02
→ Modular Monolith
→ لا نحول التواصل الداخلي إلى Microservices transport بدون سبب

T03
→ كل Business Module تملك بياناتها ومنطقها
→ IdentityAccess وInstitutions حدود Business مستقلة

T04
→ Project واحدة لكل Business Module
→ Contracts داخل نفس Module Project
→ types internal افتراضيًا
→ Module references اتجاه واحد فقط
→ Circular Dependencies ممنوعة

T05
→ Endpoint مسؤولة عن HTTP فقط
→ Handler تنظم الـUse Case
→ لا MediatR في v1
→ Direct Handler Invocation

T06
→ Domain Rules تظل داخل Domain المالكة
→ Contract لا تنقل Aggregate أو Domain Entity للخارج

T07
→ PostgreSQL 18

T08
→ DbContext/schema/migrations مستقلة لكل Module
→ لا Foreign DbContext
→ لا Cross-Module FK/Navigation/Join
→ المراجع العابرة تتحقق عبر Contracts

T09
→ InstitutionId من Client Candidate فقط
→ Tenant isolation server-side
→ لا cross-tenant data leak

T10
→ IDs لها قواعد واضحة

T11
→ DateTimeOffset UTC / IClock

T12
→ IdentityAccess تملك UserAccount / verification / security facts

T13
→ Authentication server-side
→ ICurrentActor هوية فقط
→ JWT ليست Source of Truth لAuthorization state

T14
→ Institutions تملك Membership/Role/Scope/Capabilities
→ IInstitutionContext Contract داخل Institutions.Contracts.Authorization
→ Modules الأخرى يمكنها الاعتماد على Contracts فقط

T15
→ Local transaction per Module
→ no unapproved cross-module shared transaction
→ RecordCashPayment is the only approved same-database atomic exception
→ لا network/external side effect داخل transaction

T16
→ Concurrency محلية للـstate المالكة
→ لا cross-module row locking كحل عام

T17
→ Idempotency record مملوكة للـentry/operation-owning Module
→ RecordCashPayment تحفظ StudentFinance record داخل الاستثناء الذري المعتمد
→ raw HTTP Idempotency-Key لا تصبح internal module command contract تلقائيًا

T31
→ HTTP contracts منفصلة عن internal module contracts

T32
→ Result/Error semantics
→ Infrastructure failures لا تتحول إلى Business Errors مزيفة
```

---

# 2. العلاقة مع القرارات القادمة

T18 لا تحسم كل أنواع Integration.

```text
T19 — Events & Outbox
→ asynchronous integration
→ durable publication
→ eventual consistency
→ consumer deduplication عند الحاجة

T20 — MediatR
→ القرار العام لا يغير direct typed module contracts المعتمدة هنا

T21 — Background Jobs
→ delayed/retryable work

T28 — Reporting / Read Models
→ cross-module analytical composition

T29 — Caching
→ caching/staleness policies لعقود القراءة إن تم اعتمادها لاحقًا

T34 — Logging & Observability
→ traces/metrics around module calls

T35 — Security Engineering
→ abuse/rate limits/security telemetry

T36 — Configuration & Secrets
→ timeouts/options الخاصة بالبنية عند الحاجة
```

---

# 3. ما المقصود بالتواصل المتزامن؟

التواصل المتزامن هنا يعني:

```text
Module A
→ calls typed interface from Module B
→ waits for Module B result
→ continues نفس Use Case
```

داخل:

```text
نفس ASP.NET Core process
نفس deployment
نفس request أو application operation
```

ولا يعني:

```text
HTTP call
Message broker
Eventual consistency
Background job
```

---

# 4. لماذا نحتاج T18؟

لأن حدود T08 تمنع:

```text
Foreign DbContext
Cross-module JOIN
Cross-module Entity access
```

لكن Business Use Cases ستحتاج Facts من Module أخرى.

مثال:

```text
Institutions تحتاج تعرف:
- هل UserAccount موجودة؟
- هل Active؟
- هل Phone verified؟
- هل Email verified؟
```

الـSource of Truth هي:

```text
IdentityAccess
```

إذًا نحتاج Contract صريحة بدل كسر الملكية.

---

# 5. النمط الافتراضي

النمط المعتمد:

```text
Consumer Module
→ Provider.Contracts.Interface
→ Provider internal implementation
→ Provider-owned persistence/domain
→ Minimal result
```

---

# 6. الـProvider Module هي مالكة الـContract

إذا `IdentityAccess` هي المالكة للحسابات:

```text
IdentityAccess
```

هي التي تعرف Contract مثل:

```text
IAccountFactsReader
```

وليس `Institutions`.

السبب:

- Provider تملك معنى البيانات.
- Provider تتحكم في ما تخرجه.
- Provider تمنع Consumers من فرض shape على internals.
- تغيير persistence الداخلي لا يكسر كل Modules.

---

# 7. مكان الـContracts

طبقًا لـT04:

```text
src/Modules/EduCenterOS.Modules.IdentityAccess/
├── Contracts/
├── Domain/
├── Features/
├── Infrastructure/
└── IdentityAccessModule.cs
```

ولا ننشئ افتراضيًا:

```text
EduCenterOS.Modules.IdentityAccess.Contracts.csproj
```

كProject منفصلة.

---

# 8. One Project per Module مستمرة

T18 لا تغير T04.

```text
One Business Module
→ One Module Project
```

الـContracts Folder جزء من الـProject نفسها.

---

# 9. Public Surface صغيرة

داخل الـModule:

```text
internal by default
```

يخرج `public` فقط:

- Module registration entry point عند الحاجة.
- Contract interfaces المطلوبة من Modules أخرى.
- Contract DTOs/records/enums المطلوبة لتلك interfaces.

---

# 10. Domain Entities لا تصبح Public

ممنوع:

```csharp
public class UserAccount
```

لمجرد أن `Institutions` تحتاج معلومات الحساب.

البديل:

```text
AccountFacts contract
```

---

# 11. DbContext لا تصبح Contract

ممنوع:

```csharp
public IdentityAccessDbContext DbContext { get; }
```

وممنوع حقن:

```text
IdentityAccessDbContext
```

في أي Module أخرى.

---

# 12. Repository لا تخرج عبر حدود الـModule

ممنوع Contract مثل:

```text
IUserAccountRepository
```

إذا كانت تمنح Consumer عمليات persistence عامة على Aggregate الأجنبية.

الـContract تكون Use-case/Facts oriented.

---

# 13. Query/Facts Contracts هي النمط المفضل

الأكثر شيوعًا:

```text
Consumer needs authoritative current facts
→ Synchronous read contract
```

مثال:

```text
GetAccountFacts
ValidateBranchReference
GetInstitutionAccessFacts
```

---

# 14. Commands عبر Modules استثناء

Cross-module synchronous write command ليست ممنوعة مطلقًا، لكنها:

```text
Exceptional
```

وتحتاج سبب واضح.

الـdefault:

> إذا Module أخرى تحتاج تغيير state، نراجع ownership/workflow أولًا.

## 14.1 أنواع الـCross-Module Contracts المعتمدة

T18 تميز صراحة بين:

```text
Read / Facts Contract
→ current provider-owned facts
→ no business side effects

Ordinary Command Contract
→ exceptional
→ Provider owns its local transaction
→ no shared atomicity with the Caller

Named Atomic Participant Contract
→ not an ordinary module command
→ allowed only by an explicit T15 named exception
→ currently RecordCashPayment only
```

`RecordCashPayment` تستخدم feature-specific orchestrator وparticipant contract ضيقة لكل من StudentFinance وBranchFinance. كل participant تعدل وتقفل state التي تملكها فقط، لكن الاثنتين تدخلان الـapproved atomic scope الواحدة طبقًا لـT15–T17. لا ينتج عن هذا Generic transaction API ولا صلاحية لأي Module لفتح transaction داخل Module أخرى.

---

# 15. Query لا تمنح Consumer ملكية البيانات

مجرد أن `Institutions` تستطيع قراءة `AccountFacts` لا يعني أنها تستطيع:

- تعديل Account.
- تغيير EmailVerifiedAtUtc.
- تغيير AccountStatus أو verification state.
- Revoke session.

كل ذلك يظل ملك `IdentityAccess`.

---

# 16. Consumer يعتمد على Contract فقط

مثال Project Reference مسموح:

```text
EduCenterOS.Modules.Institutions
→ EduCenterOS.Modules.IdentityAccess
```

لكن الاستخدام المسموح من `Institutions` يكون على:

```text
IdentityAccess.Contracts.*
```

لا internals.

---

# 17. اتجاه الـReference واحد

إذا اعتمدنا:

```text
Institutions → IdentityAccess
```

فممنوع إضافة:

```text
IdentityAccess → Institutions
```

لأنها Circular Dependency.

---

# 18. لا Circular Dependencies

ممنوع:

```text
A → B
B → A
```

أو:

```text
A → B → C → A
```

---

# 19. لو ظهر Circular Need

لا نحلها بتقنيات التفافية.

نراجع بالترتيب:

1. هل الـOwnership خاطئة؟
2. هل الـContract في الاتجاه الخطأ؟
3. هل العملية فعليًا asynchronous workflow؟
4. هل نحتاج Read Model/Snapshot؟
5. هل يوجد Workflow owner أو Module ثالثة طبيعية؟

---

# 20. لا Shared Contracts Project عامة

ممنوع إنشاء:

```text
EduCenterOS.Shared.Contracts
```

وتكديس Business contracts من كل Modules فيها.

ده سيعيد إنشاء Shared Kernel ضخم.

---

# 21. BuildingBlocks لا تحمل Business Contracts

ممنوع وضع:

```text
IAccountFactsReader
IInstitutionMembershipReader
InstitutionRoleCode
```

داخل:

```text
EduCenterOS.BuildingBlocks
```

إلا لو type تقنية عامة فعلًا.

---

# 22. Direct Typed DI

الـConsumer تحقن interface صريحة:

```csharp
internal sealed class SomeHandler(
    IAccountFactsReader accountFactsReader)
{
}
```

ده يجعل dependency واضحة في constructor.

---

# 23. لا Generic Module Bus

لا نعتمد:

```text
IModuleBus
IModuleClient
IModuleGateway.Send(object)
```

كطبقة عامة تخفي نوع الاعتماد.

---

# 24. لا Service Locator

ممنوع داخل Handler:

```csharp
serviceProvider.GetRequiredService(...)
```

للبحث الديناميكي عن Module service.

---

# 25. لا Reflection Dispatch

ممنوع بناء cross-module communication على:

- reflection.
- naming conventions runtime-only.
- `dynamic`.
- assembly scanning dispatch بدون typed dependency.

---

# 26. لا MediatR كحل مخفي

T05 اعتمد Direct Handler Invocation في v1.

T18 لا تضيف:

```text
IMediator.Send(...)
```

كـcross-module bus.

---

# 27. لا Internal HTTP

داخل نفس EduCenterOS host ممنوع:

```text
Institutions
→ HttpClient
→ /api/v1/account/...
```

لاستدعاء IdentityAccess.

---

# 28. أسباب منع Internal HTTP

- serialization بلا داعٍ.
- auth duplication.
- latency إضافية.
- error mapping مزدوج.
- فقد compile-time contract.
- network failure mode مصطنعة.
- خلط public API مع internal application boundary.

---

# 29. لا gRPC داخل نفس الـMonolith افتراضيًا

لا نستخدم gRPC بين Modules داخل نفس process في v1.

لو انفصلت Module إلى Service مستقلة مستقبلًا، يُعاد تقييم transport.

---

# 30. لا Message Broker لQuery تحتاج جوابًا الآن

إذا Handler تحتاج Fact authoritative الآن قبل اتخاذ القرار:

```text
Synchronous Contract
```

أوضح من إرسال Event وانتظار Projection غير مؤكدة freshness.

---

# 31. متى نستخدم Synchronous Query؟

عندما:

- نحتاج current authoritative fact.
- القرار لا يمكن أخذه من local state فقط.
- حجم البيانات محدود.
- latency داخل نفس process مقبولة.
- لا نحتاج eventual workflow.

---

# 32. متى لا نستخدم Synchronous Query؟

إذا الحاجة:

- تقرير ضخم.
- Dashboard تجميعية.
- historical analytics.
- high-volume fan-out.
- data يمكن أن تكون eventually consistent.

فغالبًا T28/Read Models أنسب.

---

# 33. Provider ترجع Facts لا Decisions تخص Consumer

مثال صحيح:

```text
IdentityAccess:
Account is Active
Phone is verified
EmailVerified = true
```

مثال غير صحيح:

```text
IdentityAccess:
CanCreateInstitution = true
```

لأن `CanCreateInstitution` قد يعتمد على:

- Institutions.
- Subscriptions/Trial.
- Platform rules.

وده Decision أوسع من Ownership IdentityAccess.

---

# 34. Fact Ownership

كل Fact في Contract يجب أن تكون:

```text
Owned by Provider Module
```

ولو الـProvider لا تملك الحقيقة بالكامل، لا تخرجها كحكم نهائي.

---

# 35. Consumer تملك Business Decision الخاصة بها

`Institutions` قد تقرأ Account Facts ثم تقرر:

```text
eligible / denied / business error
```

وفق Use Case الخاصة بها.

---

# 36. Contract ليست Entity-shaped DTO

ممنوع:

```text
UserAccountDto
```

بكل Columns الحساب لمجرد الراحة.

نفضل:

```text
AccountFacts
```

بأقل بيانات لازمة.

---

# 37. Minimum Necessary Data

قاعدة الخصوصية:

> Module لا تطلب ولا تستلم Data لا تحتاجها لاتخاذ القرار.

---

# 38. لا PII لمجرد العرض

لو `Institutions` تحتاج تعرف إن Email verified:

ترجع:

```text
IsEmailVerified = true/false
```

وليس:

```text
EmailAddress = user@example.com
```

إلا لو Use Case موثقة تحتاج القيمة نفسها.

---

# 39. لا Security Secrets

ممنوع Contract تخرج:

- PasswordHash.
- OTP/hash.
- Verification proof/hash.
- Refresh token/hash.
- Access token.
- MFA secret.
- Recovery code.
- RSA key.
- HMAC key.

---

# 40. لا Session Internals بدون احتياج

Consumer لا تحصل على:

```text
SecurityVersion
Session Version
Refresh lineage
Token hash
```

لمجرد أنها authenticated.

T13 مسؤولة عن Session validation.

---

# 41. Contract DTOs Immutable

نفضل:

```csharp
public sealed record AccountFacts(...);
```

أو Value/record immutable equivalent.

---

# 42. لا Mutable Graphs

ممنوع رجوع Object graph يمكن Consumer تعدله ثم تتوقع أن Provider تحفظه.

---

# 43. لا Lazy-loaded Objects

Contract result لا تحمل:

- Navigation Properties.
- lazy proxy.
- deferred EF query.

كل result fully materialized.

---

# 44. ممنوع IQueryable

ممنوع Contract ترجع:

```csharp
IQueryable<T>
```

لأنها تسرب query provider/persistence details وتسمح Consumer بتركيب Query على بيانات Provider.

---

# 45. ممنوع DbSet

ممنوع:

```csharp
DbSet<T>
```

أو `DbContext` أو `EntityEntry` في أي Contract عامة.

---

# 46. IEnumerable يجب أن تكون Materialized

لو Contract ترجع Collection:

تكون materialized قبل خروجها من Provider.

يفضل:

```text
IReadOnlyList<T>
```

أو collection immutable مناسبة.

---

# 47. لا EF/Npgsql Types في Contract

ممنوع تسريب:

- `DbUpdateException` كنوع result.
- Npgsql-specific row/database types.
- `NpgsqlException` كـbusiness contract.
- EF tracking metadata.

---

# 48. لا HTTP Types في Contract

ممنوع cross-module Contract ترجع:

```text
IResult
ProblemDetails
HttpResponseMessage
ActionResult
StatusCode
CookieOptions
```

---

# 49. HTTP Mapping مسؤولية Endpoint الخارجية

الـProvider contract تتعامل مع Application/Business semantics.

الـConsumer top-level flow ثم T32 تحدد HTTP response.

---

# 50. Result<T> مسموحة

يمكن استخدام `Result<T>` من BuildingBlocks إذا كانت:

- تقنية عامة.
- مستقلة عن HTTP.
- مستخدمة فعلًا في المشروع.

لكن Error codes يجب أن تكون واضحة ولا تسرب infrastructure internals.

## 50.1 Error and Exception Boundary المعتمدة

لكي لا تختلف implementations في تفسير failure، نعتمد:

```text
Expected provider-owned negative outcome
→ typed Contract result/outcome

Cancellation requested
→ OperationCanceledException / Task cancellation

Infrastructure or unexpected failure
→ exception propagates to the top-level application boundary
→ sanitized once according to T32
```

ممنوع وضع Database outage أو timeout أو programming error داخل `Result<T>` على أنها `NotFound`, `Denied` أو `Unavailable` business outcome. وممنوع catch-all داخل Provider يعيد قيمة تبدو طبيعية. لو Contract تستخدم generic `Result<T>` فيجب أن تحمل فقط outcomes المتوقعة والمملوكة للProvider؛ لا تستخدم كبديل عام للـexceptions.

---

# 51. Expected Negative Outcomes ليست Exceptions

مثال:

```text
AccountNotFound
BranchNotFound
```

لو Expected ضمن Contract يمكن تمثيلها Result/typed outcome.

أما Eligibility التي تعتمد على Consumer rules فلا تعاد كـProvider outcome؛ Provider ترجع الـfacts/status التي تملكها والـConsumer تتخذ القرار.

---

# 52. Infrastructure Failure ليست NotFound

لو PostgreSQL Provider غير متاحة:

ممنوع تحويلها إلى:

```text
AccountNotFound
NoMembership
404
```

لمجرد fail-closed.

---

# 53. Fail-Closed لا يعني Mask Outage

في Security decision:

```text
لا تمنح access عند فشل Provider
```

لكن failure نفسها تظل Infrastructure failure وتخرج وفق T32 sanitized unexpected behavior، لا False Business Denial.

---

# 54. Provider Errors لا تُفسر تلقائيًا كConsumer Errors

`IdentityAccess.AccountNotFound` لا يعني تلقائيًا إن كل Institutions endpoint ترجع نفس error code.

الـConsumer تترجمها حسب سياق Use Case.

---

# 55. Stable Contract Semantics

Contract العامة يجب أن توثق:

- ما البيانات التي ترجعها.
- هل not-found expected.
- هل result current أو snapshot.
- هل تستبعد suspended/archived state أم ترجع status.
- حدود cancellation.

---

# 56. Async by Default للI/O

أي Contract تستخدم DB أو I/O تكون:

```csharp
Task<T>
```

أو async equivalent.

---

# 57. CancellationToken إجباري

كل I/O Contract تستقبل:

```csharp
CancellationToken cancellationToken
```

كآخر parameter افتراضيًا.

---

# 58. Cancellation تمر كما هي

Consumer تمر `RequestAborted`/الـtoken المعتمدة للHandler إلى Contract.

Provider تمرها إلى EF Core/I/O.

---

# 59. لا Blocking Async

ممنوع:

```text
.Result
.Wait()
.GetAwaiter().GetResult()
```

في module communication path.

---

# 60. Cancellation ليست Business Error

Cancellation لا تتحول إلى:

```text
NotFound
Denied
Validation
```

---

# 61. DI Lifetime الافتراضية

Contract implementation التي تعتمد على DbContext تكون:

```text
Scoped
```

---

# 62. Singleton ممنوعة مع Scoped Persistence

لا نسجل provider service تعتمد على DbContext كـSingleton.

---

# 63. Transient ليست مطلوبة بلا سبب

لو service لها request-scoped dependencies، نفضل Scoped الواضحة.

---

# 64. Provider تسجل implementation بنفسها

`IdentityAccessModule`/registration الخاصة بها تسجل:

```text
IAccountFactsReader → AccountFactsReader
```

الـAPI Composition Root لا تعرف implementation class الداخلية.

---

# 65. Implementation تظل Internal

مثال:

```csharp
internal sealed class AccountFactsReader : IAccountFactsReader
```

والـinterface هي التي تكون `public` عند الحاجة.

---

# 66. Same Module لا تجبر نفسها على Public Contract

داخل `IdentityAccess` نفسها يمكن استخدام internal query/service المناسبة.

الـpublic Contract موجودة لعبور الـModule boundary فقط.

---

# 67. No Ambient Service Lookup

الـProvider لا تعتمد على ambient static locator للحصول على current dependencies.

كل dependency صريحة في constructor.

---

# 68. ICurrentActor تظل T13 Contract

T18 لا توسع `ICurrentActor`.

```text
UserAccountId
UserSessionId
```

فقط.

---

# 69. IInstitutionContext تظل T14 Contract

T18 لا تنقلها إلى BuildingBlocks.

مكانها:

```text
Institutions.Contracts.Authorization
```

---

# 70. Consumer لا تبني IInstitutionContext بنفسها

Modules الأخرى قد تستهلك validated Institution Context، لكنها لا تصنعها من request data.

---

# 71. Request IDs ليست Authority

Cross-module contract لا تعتبر:

```text
userAccountId from body
institutionId from body
branchId from body
```

proof of authorization.

---

# 72. Current-user Identity Server-derived

في current-user flows:

```text
UserAccountId
```

تأتي من `ICurrentActor`.

---

# 73. Institution Context Server-validated

في institution-scoped flows، trusted tenant facts تأتي من:

```text
IInstitutionContext
```

بعد T14/T09 validation.

---

# 74. Contract لا تعيد Authorization State للJWT

Cross-module communication لا تستخدم لتعبئة JWT بـ:

- Role.
- Permission.
- InstitutionId.
- Branch scope.
- Capabilities.

---

# 75. No Authorization Cache في Sprint 03

Account/Membership authorization facts التي يلزم أن تكون current تُقرأ server-side لكل request حسب العقود المعتمدة.

Caching مؤجل لـT29 وبعد threat/staleness review.

---

# 76. Fact Read لا تعني Transactional Atomicity عبر Modules

دي قاعدة أساسية:

```text
Read Provider Fact
↓
time passes
↓
Consumer commits local state
```

Provider fact قد تتغير بين المرحلتين.

---

# 77. TOCTOU يجب أن تكون واعية

`Time Of Check / Time Of Use` بين Modules لا تُخفى.

لو business invariant لا تتحمل تغير الـFact بعد التحقق، synchronous read وحدها غير كافية.

---

# 78. لا Shared Transaction غير معتمدة لحل TOCTOU

الـdefault ممنوع حل المشكلة بـ:

```text
Consumer DbContext + Provider DbContext
→ one shared DbTransaction
```

الاستثناء الوحيد في Core V1 هو `RecordCashPayment` بالـatomic-participant protocol المعتمدة في T15–T17. هذا الاستثناء لا يبرر استخدام Shared Transaction لأي TOCTOU أخرى ولا يتوسع بالقياس؛ أي استثناء جديد يحتاج تعديلًا صريحًا في T15 وT18.

---

# 79. لا Cross-Module Row Lock

ممنوع Consumer تقول Provider:

```text
lock this row and keep it locked while I write my module
```

كـdefault architecture.

في `RecordCashPayment` لا تخرج أي Row Lock من مالكتها: BranchFinance participant تقفل `CashDrawer` ثم `CashShift` داخل الـapproved atomic scope، وStudentFinance لا ترى أو تقفل جداول BranchFinance. هذا تنسيق Commit مشترك، وليس Foreign lock ownership.

---

# 80. لو نحتاج Atomic Cross-Module Invariant

نعمل Architecture Review:

1. هل state يجب أن تكون في نفس Module؟
2. هل Aggregate/ownership ناقصة؟
3. هل العملية workflow متعددة الخطوات؟
4. هل eventual consistency مقبولة؟
5. هل T19 Outbox/Event أفضل؟

ولو ظل same-database atomicity هو الحل الوحيد الصحيح، لا يصبح مسموحًا إلا كـnamed T15 exception لها orchestrator وparticipants وlock order واختبارات PostgreSQL محددة. لا توجد استثناءات ضمنية.

---

# 81. Local Transaction تبدأ بعد Cross-Module Reads قدر الإمكان

طبقًا لـT15:

```text
validate request
↓
call synchronous provider facts
↓
perform local decision
↓
start local explicit transaction only if needed
↓
local persistence
```

الـnamed `RecordCashPayment` exception لا تتبع هذا المسار العام؛ تبدأ approved atomic scope ثم تستدعي participants المحددة بالترتيب الموثق في T15–T17.

---

# 82. لا Module Call أثناء Local Transaction افتراضيًا

ممنوع فتح local transaction/locks ثم انتظار Module أخرى إلا لو Use Case موثقة جدًا ومراجعة.

الـdefault:

```text
No cross-module call while local transaction is open
```

الاستثناء الوحيد هو استدعاء `RecordCashPayment` atomic participants من الـfeature-specific orchestrator داخل الـapproved shared PostgreSQL transaction. لا يسمح داخلها بأي arbitrary module query أو network/external call.

---

# 83. سبب القاعدة

لتقليل:

- lock duration.
- deadlock graphs.
- hidden transaction coupling.
- failure complexity.
- retry ambiguity.

---

# 84. Provider Query قد تستخدم Transaction داخلية خاصة بها

Implementation Provider يمكنها استخدام transaction حسب T15/T16 داخل حدودها.

Consumer لا تراها ولا تتحكم فيها.

---

# 85. Provider Lock لا يخرج عبر Contract

لو Provider تستخدم `FOR UPDATE` داخليًا:

- lock ملك Provider.
- transaction ملك Provider.
- لا تسلم handle للConsumer.
- لا تطلب من Consumer إكمال transaction.

Atomic participant المعتمدة لا تعيد lock handle كذلك؛ هي تنفذ lock/write المملوكين لها داخل الـatomic scope وتعود بنتيجة participant محددة، بينما orchestrator وحدها تدير Commit/Rollback للـscope كلها.

---

# 86. Ordinary Cross-Module Synchronous Write لا تكون Atomic مع Consumer Write

مثال:

```text
Module A local commit
+
Module B local commit
```

ليستا Transaction واحدة.

هذا هو حكم الـordinary command contracts. `RecordCashPayment` ليست ordinary command chain؛ هي الـnamed atomic-participant exception الوحيدة المعتمدة حاليًا.

---

# 87. لا ندعي Atomicity غير موجودة

ممنوع توثيق workflow كهذا على إنه:

```text
all-or-nothing
```

إذا فيه commits محلية منفصلة.

---

# 88. Synchronous Command من Module إلى أخرى

لو تم اعتمادها في Use Case محددة:

```text
Caller calls Provider Command Contract
→ Provider validates
→ Provider owns its local transaction
→ Provider commits/returns result
```

لا ينطبق هذا الوصف على Named Atomic Participant Contract؛ الـparticipant لا تملك Commit منفردة، بل تنفذ داخل scope يديرها orchestrator المعتمدة.

---

# 89. Consumer لا تفتح Provider Transaction

ممنوع API مثل:

```text
BeginProviderTransaction()
CommitProviderTransaction()
```

في Contracts.

وفي الاستثناء المعتمد لا توجد APIs عامة من نوع Begin/Commit على Provider. الـfeature-specific orchestrator تنشئ scope واحدة من factory تقنية مقيدة، والـparticipants تدخل فيها دون امتلاك Commit/Rollback أو كشف DbContext الخاصة بها.

---

## 89.1 RecordCashPayment Atomic Participant Boundary

الشكل التنفيذي المعتمد:

```text
RecordCashPaymentOrchestrator
  - owned by StudentFinance application layer
↓ creates
RecordCashPaymentAtomicScope
  - owns one open NpgsqlConnection
  - owns one NpgsqlTransaction
  - owns Commit / Rollback
↓ passes an opaque enlistment handle
StudentFinance participant
BranchFinance participant
↓
Each participant enlists only its owned DbContext
and performs only its owned locks/writes
```

اتجاه الاعتماد يظل واحدًا:

```text
StudentFinance
→ BranchFinance.Contracts
→ narrow cash-movement atomic participant contract

BranchFinance
↛ StudentFinance
```

StudentFinance تملك Endpoint/Handler/orchestrator وT17 idempotency record وPayment. الجزء المحلي منها يظل internal. BranchFinance تملك public participant contract الضيقة اللازمة للعبور بين الـProjects وinternal implementation التي تقفل Drawer/Shift وتنشئ CashMovement. الـcommand تستخدم BranchFinance-owned contract codes وscalar source reference/PaymentId؛ لا تحتاج BranchFinance reference إلى StudentFinance Entity أو Project.

القواعد:

- الـpublic participant interfaces تكون feature-specific وليست Repository أو UnitOfWork عامة.
- الـopaque enlistment handle تكون Technical BuildingBlock صغيرة، لا تحمل Business contracts، ولا تكشف `DbContext` أو raw SQL للـorchestrator. الـscope تحتفظ بCommit/Rollback capability، بينما الـparticipant تحصل فقط على enlistment capability.
- الـhandle لا تمنح participants `Commit` أو `Rollback`؛ هاتان العمليتان ملك الـorchestrator فقط.
- participant implementation وحدها تستعمل الـhandle لإدخال DbContext المملوكة لها في نفس provider-supported PostgreSQL transaction.
- لا يجوز resolve أو استخدام الـhandle خارج `RecordCashPaymentOrchestrator`، ويثبت ذلك Architecture test.
- ترتيب التنفيذ والـlocks والـidempotency claim يظل كما في T15–T17.
- فشل أي participant أو cancellation قبل Commit يعمل Rollback للـscope كلها.
- بعد بدء Commit تطبق Commit-ambiguity reconciliation من T17 باستخدام نفس `OperationId`؛ لا نفترض Rollback بسبب cancellation أو connection loss.

هذا هو الاستثناء الوحيد في T18 الذي يسمح بتمرير approved transaction-enlistment capability عبر module boundary. لا يسمح بتمرير `DbContext` أو `DbConnection` أو `DbTransaction` نفسها كـBusiness contract parameters.

---

# 90. Partial Success يجب أن تكون Business-defined

لو Module A نجحت وModule B فشلت:

إما:

- الحالة مقبولة وممثلة صراحة.
- compensation موثقة.
- retry/idempotency موثقة.
- أو التصميم غير صالح ويجب تغييره.

---

# 91. T19 هي المسار الطبيعي للCross-Module Follow-up

لو state change في Module A يجب أن يؤدي إلى عمل في B بعد commit:

```text
A commit
+
Outbox/Event
→ B لاحقًا
```

عند تنفيذ T19.

---

# 92. لا Publish Event قبل Commit

T15 مستمرة.

Event عن state لم تلتزم بعد ممنوعة.

---

# 93. Synchronous Query vs Event Projection

إذا نحتاج:

```text
Current authoritative fact الآن
```

نستخدم Query Contract.

إذا نحتاج:

```text
Eventually updated local/read model
```

نستخدم Events/Projection عند تنفيذها.

---

# 94. Snapshot التاريخية مختلفة

إذا Module تحتاج قيمة تاريخية وقت العملية:

يمكنها تخزين Snapshot محدودة طبقًا لـT08.

لا تجعلها current Source of Truth.

---

# 95. لا Mirror Tables لمجرد تجنب Contract

ممنوع نسخ كل UserAccounts داخل Institutions لمجرد عدم عمل synchronous query.

---

# 96. Read Model ليست Source of Truth للAuthorization

Reporting/read projection لا تستخدم لمنح current security authorization إلا لو قرار أمني مستقل صريح يضمن freshness.

---

# 97. No N+1 Cross-Module Calls

ممنوع pattern:

```text
load 500 memberships
for each membership:
    call IdentityAccess.GetAccountFacts(id)
```

---

# 98. Bulk Contract عند الحاجة

لو Use Case حقيقية تحتاج Facts لعدة IDs، Provider قد تعرف Contract bulk bounded مثل:

```text
GetAccountFactsBatchAsync(ids)
```

بعد مراجعة حجم وحدود الطلب.

---

# 99. Bulk Contracts ليست Unbounded

لا نقبل list بلا حد في request داخلية.

Provider تحدد maximum عملي إذا لزم.

---

# 100. Pagination لLists الكبيرة

لو Contract نفسها تعرض collection كبيرة، تستخدم pagination/cursor/limit مناسب بدل materialize كل rows.

---

# 101. لا Lazy Stream عبر DbContext lifetime

لا نرجع async enumerable مربوط بـDbContext طويلة العمر بدون قرار صريح.

الـdefault result materialized.

---

# 102. Call Count جزء من التصميم

كل Use Case يجب أن تعرف كم cross-module call تقريبًا تنفذ.

Hidden fan-out مرفوض.

---

# 103. Query Contract تكون Narrow

يفضل:

```text
GetAccountFacts(userAccountId)
```

على:

```text
QueryIdentityAccess(any filter/expression)
```

---

# 104. لا Generic Predicate Contract

ممنوع تمرير:

- Expressions.
- SQL fragments.
- arbitrary filters.

من Consumer إلى Provider.

---

# 105. لا Query Builder عبر Modules

Provider هي اللي تحدد query semantics، لا Consumer.

---

# 106. Contract Input صغيرة وواضحة

Inputs تكون IDs/value parameters المطلوبة فقط.

لا نمرر entire HTTP DTO لمجرد الراحة.

---

# 107. HTTP DTO != Module Contract DTO

Request/Response DTO الخاصة بـAPI لا يعاد استخدامها تلقائيًا كـinternal module contract.

السبب:

```text
Public API evolution
≠
Internal module semantics
```

---

# 108. لا API version suffix داخل Internal Contract افتراضيًا

لا نسمّي:

```text
IAccountFactsReaderV1
```

لمجرد أن HTTP API هي `/api/v1`.

---

# 109. Contract Evolution داخل Monorepo

بما أن Modules تُبنى معًا:

- breaking internal contract change يمكن تنفيذه atomically في commit واحد.
- Compiler يكشف Consumers المكسورة.
- لا نحتاج network-style versioning افتراضيًا.

---

# 110. Public لا تعني Public API خارجية

`public` هنا تعني Assembly visibility للModules الأخرى.

ليست بالضرورة endpoint أو SDK contract.

---

# 111. Public Contract تحتاج Documentation

أي public interface/record/enum بين Modules يجب أن يكون واضح المعنى والownership.

---

# 112. Naming

نفضل أسماء تعكس semantic purpose:

```text
IAccountFactsReader
IInstitutionAccessResolver
IBranchReferenceReader
```

على أسماء عامة مثل:

```text
IIdentityAccessService
IInstitutionService
IDataService
```

---

# 113. Avoid God Service

Interface بـ20-50 method من Business areas مختلفة مرفوضة.

قسّم حسب capability/use-case family.

---

# 114. Query وCommand لا تختلط بلا داعٍ

لو interface بدأت تجمع reads+writes متعددة غير مترابطة، نعيد تقسيمها.

---

# 115. Bool-only Results قد تكون غامضة

بدل:

```text
bool IsValid(id)
```

لو Consumer تحتاج أكثر من سبب/Fact، نرجع structured contract.

---

# 116. Null Semantics موثقة

لا نستخدم `null` بمعانٍ متعددة مثل:

```text
not found / suspended / unavailable / unauthorized
```

Result shape تكون واضحة.

---

# 117. Guid.Empty ليست Sentinel

لا نرجع `Guid.Empty` بمعنى not found/no relationship.

نستخدم explicit result/nullable حسب المعنى.

---

# 118. Date/Time Contracts

أي timestamps بين Modules تكون:

```text
DateTimeOffset
UTC
```

وفق T11.

---

# 119. Time-dependent Facts

Provider هي المالكة لتفسير state الخاصة بها حسب `IClock`.

لو ترجع raw effective dates للConsumer، يكون ذلك لأن Consumer تحتاجها فعلًا، لا لكي تعيد تنفيذ نفس rule بلا داعٍ.

---

# 120. Enum Contracts مستقلة عن Domain enum

لو Consumer تحتاج code محدود:

يمكن تعريف public contract enum/code.

لا نجعل Domain enum `public` لمجرد reuse.

---

# 121. Contract Codes Stable داخل المشروع

Role/status/permission code التي تعبر module boundary تتغير بحذر لأنها dependency compile-time ومعنوية.

---

# 122. No Magic Strings بلا Contract

لا نمرر string مثل:

```text
"Active"
"General"
```

بدون type/code contract واضحة عند الحاجة.

---

# 123. Account Facts Contract — الغرض

في Sprint 03 تحتاج `Institutions` Facts من `IdentityAccess` لبناء institution authorization/workspace behavior.

الـContract يجب أن تكون read-only.

لا تستدعى تلقائيًا في كل authenticated request: T13 تتحقق أصلًا من Account/Session للحساب الحالي. تستخدم فقط عندما تحتاج الـUse Case Fact إضافية مملوكة لـIdentityAccess، أو lookup لحساب ليس مجرد إثبات الـcurrent authentication.

---

# 124. Account Facts — البيانات المسموحة مبدئيًا

الحد الأدنى المتوقع، حسب Use Cases الحالية:

```text
UserAccountId
AccountStatus fact when the Use Case needs it beyond T13
PhoneVerified fact
EmailVerified fact
```

بالـshape التي يثبتها التنفيذ النهائي.

---

# 125. Account Facts — البيانات غير المطلوبة

لا نخرج في Sprint 03:

```text
Phone number
Email address
Full name
Password hash
SecurityVersion
Session state
Refresh state
MFA secrets
Recovery codes
```

بدون Use Case موثقة.

---

# 126. Active Account Fact لا تستبدل T13

T13 تظل مسؤولة عن Authentication/session validation لكل protected request.

`AccountFacts.Active` داخل Institutions لا تصبح بديلًا عن Authentication middleware.

ولا نعيد نفس Account-active query بلا حاجة إذا كانت T13 ضمنت الشرط للـcurrent protected request ولا توجد Business rule إضافية تعتمد على Status.

---

# 127. Duplication of Security Check

وجود fact مثل Active قد يكون مطلوبًا للـBusiness/Authorization eligibility، لكن لا نقول إن Institutions هي التي تتحقق من session.

---

# 128. Account Missing في Current Actor Path

لو `ICurrentActor.UserAccountId` لا توجد داخل IdentityAccess بشكل غير متوقع:

- لا يمنح access.
- لا يتحول تلقائيًا إلى fake success.
- Consumer تتعامل fail-closed.
- Infrastructure inconsistency تُسجل/تعالج وفق السياسات المناسبة.

---

# 129. Suspended/Closed Facts

Provider تملك معنى account status وتعرض contract code/fact محدودة إذا Consumer تحتاجها.

Consumer لا query جدول UserAccount مباشرة.

---

# 130. Verification Facts

Provider تملك:

```text
Phone verification
Email verification
```

Consumer تطلب booleans/facts، لا verification entities/challenges.

---

# 131. No Verification Mutation من Institutions

`Institutions` لا تغير:

```text
EmailVerifiedAtUtc
PhoneVerifiedAtUtc
```

---

# 132. Sprint 03 Dependency Direction

الـdirection المقصودة:

```text
Institutions
→ IdentityAccess.Contracts
```

---

# 133. IdentityAccess لا تعتمد على Institutions

Sprint 03 لا تضيف reference عكسية.

---

# 134. Institution Context Provider Direction

مستقبلًا Modules مثل Academic قد تعتمد:

```text
Academic
→ Institutions.Contracts.Authorization
```

للحصول على validated institution context/authorization abstractions.

---

# 135. Institutions لا تصبح Shared Kernel

حتى لو عدة Modules تعتمد على Institutions Contracts، لا نضع فيها Contracts لا تملكها Business-wise.

---

# 136. Cross-Module Authorization

Provider query لا تمنح permission لConsumer operation إلا لو authorization نفسها مملوكة للProvider.

مثال:

```text
Institutions owns institution membership authorization
```

فـAcademic يمكنها الاعتماد على Institutions authorization contracts.

---

# 137. Consumer Business Rule تبقى Consumer-owned

مثال:

```text
Academic decides whether a new StudyGroup can be created
```

Institutions قد تؤكد:

- institution/branch relation.
- authorization context.

لكن لا تملك academic scheduling rules.

---

# 138. Provider لا يعرف Consumer Use Case بلا داعٍ

لا نعمل Contract في IdentityAccess باسم:

```text
CanInstitutionsCreateBranchForUser
```

لأنها coupling مع Use Case خارجية.

---

# 139. Anti-Corruption at Contract Boundary

Consumer تعتمد على Contract DTOs وليس Domain model الأجنبية.

ده يعمل Anti-Corruption Boundary بسيطة داخل الـMonolith.

---

# 140. No Shared Domain Object Graph

لا object graph تعبر عدة Modules.

مثال ممنوع:

```text
Institution
  → UserAccount entity
  → UserSession entity
```

---

# 141. Cross-Module IDs

Module تحتفظ Foreign IDs كـscalar IDs طبقًا لـT08.

لكن ID وحدها لا تثبت existence أو authorization.

---

# 142. Reference Validation

عند إنشاء local record يحمل Foreign ID:

- validate through provider contract إذا current existence/relation مطلوبة.
- احفظ ID فقط.
- لا تنشئ cross-module FK.

---

# 143. Historical Reference

لو الـForeign entity تغيرت/حُذفت لاحقًا، local historical record قد يحتفظ ID/snapshot حسب Business rule.

T18 لا تفرض cascade عبر Modules.

---

# 144. No Cascading Delete عبر Contract افتراضيًا

حذف Entity في Provider لا يستدعي synchronously deletes في كل Consumers كـdefault.

ده workflow/event concern.

---

# 145. No Hidden Side Effects في Query

Contract اسمها Reader/Query لا تعدل Business state.

---

# 146. LastSeen/Session Activity

Account Facts query لا تحدث:

- Session LastSeen.
- Idle expiry.

T13 حسمت sliding على successful Refresh فقط.

---

# 147. No Audit Side Effect داخل Read Contract افتراضيًا

Audit security events الخاصة بالقراءة إن وجدت تُحسم T27/T34، لا تجعل query business-mutation سرية.

---

# 148. Current Facts vs Snapshot

Contract يجب أن تعرف نفسها:

```text
Current authoritative read
```

أو:

```text
Historical snapshot
```

ولا نخلط الاثنين.

---

# 149. No Caching Assumption

Consumer لا تفترض إن Provider result ثابتة لباقي session.

---

# 150. No Store-in-JWT Shortcut

لا نحول cross-module read إلى JWT claims لتجنب call.

T13/T14 تمنع authz state في JWT.

---

# 151. No Store-in-Session Shortcut

لا نخزن membership/role/account facts داخل UserSession كـSource of Truth لمجرد تقليل calls.

---

# 152. Caching لاحقًا

لو T29 اعتمد caching لعقد معينة:

- Provider تحدد freshness semantics.
- security-sensitive contracts تحتاج review خاص.
- cache لا تغير ownership.

---

# 153. Application-Level Composition

Use Case يمكنها جمع data من local module + synchronous facts من provider داخل Handler المالكة للUse Case.

---

# 154. API ليست Orchestrator للBusiness

`EduCenterOS.Api` لا تعمل:

```text
call Module A
call Module B
combine business decision
```

داخل endpoint كBusiness logic.

الـHandler/Module owner تنظم use case.

---

# 155. Presentation-only composition

لو مستقبلًا يوجد response تجميعي purely presentation/read-only ولا Module طبيعية تملكه، يتم تصميم Query/Read Model أو composition layer بصورة صريحة، وليس إدخال Business rules في API.

---

# 156. Workflow Owner

أي cross-module operation لها top-level Business owner واضحة.

هي التي:

- تنظم الخطوات.
- تتعامل مع outcomes.
- لا تملك persistence الخاصة بالModules الأخرى.

---

# 157. لو لا يوجد Owner واضح

ده design smell.

نراجع Module boundaries بدل إنشاء Coordinator عام.

---

# 158. No Global Orchestration Service

ممنوع:

```text
BusinessWorkflowService
SystemCoordinator
EverythingService
```

يجمع كل Modules.

---

# 159. Call Graph يجب أن تكون مقروءة

من constructor والProject references نقدر نعرف:

```text
Who depends on whom
```

---

# 160. Provider لا تعمل Callback للConsumer

لو A تستدعي B، B لا تستدعي A داخل نفس path لتكملة العملية.

ده circular runtime coupling حتى لو compiler dependency ملتفة.

---

# 161. Deep Call Chains مرفوضة افتراضيًا

نبتعد عن:

```text
A → B → C → D → E
```

لأنها تسبب:

- latency مخفية.
- failure coupling.
- صعوبة tracing.
- deadlock/transaction risks.

---

# 162. Provider implementation تستخدم بياناتها فقط افتراضيًا

Public Contract implementation لا تحول نفسها إلى orchestrator يستدعي Modules أخرى بدون توثيق.

---

# 163. Transitive Dependency لا تُخفى

لو Contract B تحتاج C فعلًا، dependency يجب أن تكون واضحة ومراجعة، لا runtime service locator.

---

# 164. No General Transaction Propagation

Contract method لا تقبل:

```text
DbTransaction
IDbContextTransaction
TransactionScope
DbConnection
```

من Consumer.

هذه قاعدة العقود العادية. الاستثناء الوحيد هو الـopaque enlistment capability داخل `RecordCashPaymentAtomicScope` في القسم 89.1؛ لا تكشف raw transaction types ولا يمكن استخدامها لإنشاء workflow عامة.

---

# 165. No SaveChanges Callback

Provider لا ترجع delegate تقول للConsumer متى يعمل Save/Commit.

---

# 166. No Shared ChangeTracker

لا Entity tracking يعبر DbContexts/Modules.

---

# 167. Concurrency Exceptions تبقى Provider concern

Provider تحسم T16 على state الخاصة بها.

Consumer لا تتعامل مع EF concurrency token الأجنبية مباشرة.

---

# 168. Consumer لا تعيد Provider Transaction عميانيًا

لو Contract write فشلت بسبب concurrency/deadlock، provider contract يجب أن تعطي outcome واضح أو ترمي infrastructure failure حسب السياسة؛ Caller لا تعمل blind retry لكل operation.

---

# 169. HTTP Idempotency-Key لا تمر افتراضيًا للProvider

T17:

```text
raw HTTP Idempotency-Key
```

ملك entry HTTP intent، وليست global business key لكل Modules.

---

# 170. Internal Operation Identity

أي synchronous cross-module command قد يسبب Duplicate effect مؤذية أو Commit ambiguity يجب أن تعرف workflow-owned `OperationId`:

```text
Canonical non-empty UUID
Generated before the first command attempt
Stable across every retry/reconciliation attempt
Persisted by the Provider atomically with the effect
Protected by a provider-owned unique constraint in the documented scope
```

الـProvider توفر lookup/reconciliation contract بالـOperationId عندما يمكن أن تضيع النتيجة بعد Commit. الـHTTP `Idempotency-Key` الخام لا تمر، لكن الـentry operation يمكن أن تمرر الـsemantic `OperationId` المعتمدة في T17 إذا كانت هي نفس Business operation.

Command لا تحمل duplicate risk ولا يمكن retry لها لا تجبر على OperationId بلا فائدة؛ القرار يوثق في Contract DoD.

---

# 171. Exactly-once غير مضمون

T18 لا تعد exactly-once delivery بين Modules. عند تطبيق OperationId + provider uniqueness تكون الضمانة المستهدفة:

```text
At-least-once attempt
+
At-most-one committed business effect per OperationId scope
```

---

# 172. Retry Boundaries

Retries تخضع:

- T15 transaction.
- T16 concurrency.
- T17 idempotency.
- Provider contract semantics.

لا global retry decorator لكل module calls.

---

# 173. In-process Call لا تحتاج Network Retry

لأن call داخل نفس process.

لو Provider DB تعرض transient failure، policy تكون persistence/infrastructure policy داخل Provider، لا HTTP-style retry بين Modules.

---

# 174. Cancellation أثناء Provider Query

لو cancellation قبل completion:

- لا Consumer تكمل business mutation.
- Provider تمر cancellation للDB.

---

# 175. Cancellation أثناء Provider Command

لو command mutation تم Commit ثم cancellation حدثت، outcome قد تكون committed؛ لا نفترض rollback لمجرد cancellation.

لو العملية مادية، Caller تعمل reconciliation بنفس `OperationId` ولا ترسل intent جديدة. T15/T17 rules مستمرة.

---

# 176. Error Namespaces

يفضل stable module-owned error codes عند خروج error متوقعة عبر Contract، مثل:

```text
IdentityAccess.Account.NotFound
Institutions.Branch.NotFound
```

إذا كانت مفيدة للConsumer.

---

# 177. Consumer لا تعرض Internal Error تلقائيًا

Internal module error code ليست بالضرورة public HTTP code.

Top-level Use Case تحدد ما يظهر للخارج.

---

# 178. No Database Error Text

لا Contract تعيد:

- SQL.
- constraint message raw.
- table name internal بلا حاجة.
- stack trace.

---

# 179. Security Enumeration

Cross-module contract قد تعرف object exists، لكن Consumer يجب ألا تحولها إلى external existence leak.

T14 401/403/404 policy تظل authoritative.

---

# 180. Tenant-aware Contracts

لو Provider contract institution-scoped:

لا تثق في raw institutionId القادمة من client بدون validated context/path موثقة.

---

# 181. Candidate vs Trusted Institution

نميز بين:

```text
Candidate InstitutionId
```

و:

```text
Validated IInstitutionContext.InstitutionId
```

---

# 182. Bootstrap Exception

بعض Contracts داخل Institutions نفسها قد تكون مسؤولة عن تحويل Candidate إلى Context موثقة.

دي تكون محددة جدًا وتطبق fail-closed predicates طبقًا لـT14/T09.

---

# 183. IgnoreQueryFilters لا تعبر Boundary

Consumer لا تطلب من Provider:

```text
ignore tenant filters
```

Provider وحدها تقرر bootstrap/admin exceptions المسموحة.

---

# 184. No Consumer-controlled Security Flags

ممنوع parameter مثل:

```text
skipAuthorization = true
ignoreTenant = true
includeSuspended = true
```

في public business Contract إلا لو contract administrative موثقة جدًا ومؤمنة.

---

# 185. No Client Round-trip Authority

لو response من `/api/v1/me/institutions` فيها role/capabilities للUI، الـClient لا تعيدها في request لتصبح authority.

كل request تعيد server-side lookup.

---

# 186. Cross-Module Data and Privacy

كل Contract تخضع لـBusiness 14:

- minimum necessary.
- purpose limitation.
- no secrets.
- no unnecessary PII.

---

# 187. Logs لا تسجل Contract Payload كاملة افتراضيًا

خصوصًا Account/Identity facts.

---

# 188. Correlation ليست Parameter Business

لا نمرر `X-Correlation-Id` داخل Contract method كbusiness parameter.

Observability تستخدم ambient request/activity infrastructure لاحقًا.

---

# 189. TraceId ليست Domain Data

لا تحفظ trace/correlation داخل domain entities لمجرد cross-module call.

---

# 190. No Per-call Info Log Spam

لا نسجل كل contract query كInfo بشكل افتراضي.

T34 تحدد observability/metrics.

---

# 191. Performance — In-process لا تعني Free

كل contract query قد تؤدي لـDB roundtrip.

لذلك:

- قلل call count.
- project minimum columns.
- index provider queries.
- تجنب N+1.

---

# 192. Provider Query Projection

نفضل query مباشرة إلى Contract projection عند الملاءمة بدل materialize Aggregate كاملة إذا لا نحتاج Domain behavior.

مع الحفاظ على rules/filters اللازمة.

---

# 193. AsNoTracking للRead Contracts

للقراءة فقط، Provider غالبًا تستخدم:

```text
AsNoTracking
```

إذا لا يوجد سبب tracking.

---

# 194. Consumer لا تختار Query Strategy

لا تقول للProvider:

```text
use tracking
use index X
include Y
```

دي persistence implementation details.

---

# 195. Compiled Query/Optimization لاحقًا عند القياس

لا نضيف optimization مبكر لمجرد أن Contract cross-module.

---

# 196. Timeout

لا نضيف timeout مختلف لكل internal call افتراضيًا.

DB/provider timeouts تتبع configuration policy لاحقًا.

---

# 197. Resilience

In-process module call لا يستخدم Circuit Breaker/Polly كأنه remote service.

لو Module فصلت لاحقًا إلى process مستقل، يعاد القرار.

---

# 198. Availability Coupling مقبول داخل Modular Monolith

الـModules داخل نفس deployment أصلًا تشترك في process availability.

لا نمثلها كremote services وهمية.

---

# 199. Build-time Coupling مقصودة ومحدودة

Project Reference direct هي coupling معلنة.

نقبلها عندما Business dependency حقيقية ومراجعة.

---

# 200. Contract Surface تقلل coupling

الهدف ليس zero coupling؛ الهدف:

```text
Explicit, narrow, owned coupling
```

---

# 201. Architecture Tests — Project References

يجب اختبار/مراجعة:

- BuildingBlocks لا تعتمد Modules.
- Api composition root فقط تشير للModules.
- allowed Module→Module references مع allowlist.
- لا cycles.

الـallowlist تكون Version-controlled ويفشل Architecture test عند ظهور dependency edge غير مسجلة؛ لا نكتفي بمراجعة يدوية للرسم.

---

# 202. Architecture Tests — Public Surface

نراجع إن Domain/Infrastructure types الحساسة لا تصبح public بلا سبب.

القاعدة القابلة للاختبار: public types داخل Business Module تقتصر على namespaces المعتمدة تحت `Contracts` وModule registration entry point وأي technical type تفرضها tooling وموجودة في allowlist ضيقة مثل generated EF migration عند الحاجة. Entity/DbContext/Repository/EF configuration أو implementation class عامة بلا استثناء موثق تفشل الاختبار.

---

# 203. Architecture Tests — DbContext Injection

يمنع Handler في Module A من الاعتماد على:

```text
ModuleBDbContext
```

---

# 204. Architecture Tests — Entity References

يمنع Domain/Feature في Consumer من الاعتماد على Provider Entity types.

---

# 205. Static Audit — Internal HTTP

نراجع عدم وجود `HttpClient` calls للـAPI المحلية لمجرد module communication.

---

# 206. Unit Tests للConsumer

Consumer Handler يمكن اختبارها بـfake/stub للContract العامة.

لا تحتاج Provider DbContext في Unit Test الخاصة بها.

---

# 207. Unit Tests للProvider

Provider contract behavior تُختبر على service/query logic حسب الحاجة.

---

# 208. Integration Tests للBoundary

Critical cross-module flow تُختبر من خلال DI الحقيقية مع Provider implementation الحقيقية وPostgreSQL الحقيقية عند الحاجة.

---

# 209. لا EF InMemory لإثبات Boundary Persistence

إذا correctness تعتمد على PostgreSQL query/constraints/filters، نستخدم provider الحقيقية.

---

# 210. Failure Integration Tests

نختبر:

- expected not-found/fact state.
- cancellation حيث مهم.
- provider infra failure mapping عند وجود harness.
- no partial consumer mutation قبل successful provider decision.

ولـ`RecordCashPayment` نضيف Integration Tests صريحة تثبت:

- كل participant تستخدم DbContext المملوكة لها داخل نفس PostgreSQL transaction.
- failure بعد أي participant وقبل Commit تعمل Rollback لكل Payment/CashMovement/Idempotency/Outbox state.
- idempotency claim تسبق business locks.
- `CashDrawer → CashShift` lock ordering مستمرة.
- commit ambiguity تتصالح بنفس `OperationId` ولا تنشئ effect ثانية.

---

# 211. Cross-Tenant Tests

Institution-scoped contracts يجب أن تثبت عدم cross-tenant leakage.

---

# 212. Parallel Request Tests

Scoped contexts/services لا تتشارك state بين requests المتوازية.

---

# 213. Contract Shape Tests

لـsecurity-sensitive public contracts يمكن استخدام reflection/architecture tests لمنع توسع surface بلا مراجعة.

---

# 214. No InternalsVisibleTo بين Modules

`InternalsVisibleTo` لا تستخدم لتجاوز Contracts بين Business Modules.

مسموحة للاختبارات فقط عند سبب واضح.

---

# 215. No Friend Assembly Shortcut

ممنوع جعل Module أخرى friend assembly للوصول للEntities/Infrastructure.

---

# 216. No Reflection to Internal Types

ممنوع Consumer تستخدم reflection للحصول على internal services/fields.

---

# 217. No Raw SQL Cross Schema

حتى لو Contract implementation في Consumer تعرف أسماء جداول Provider، ممنوع query مباشرة.

---

# 218. No Database View كBypass

لا ننشئ View داخل Consumer تقرأ Provider schema فقط لتجاوز Contract، إلا Read Model/Reporting موثقة في decision مخصصة.

---

# 219. No Cross-Module Foreign Key

T08 مستمرة.

Synchronous validation لا تغير قرار عدم الـFK العابرة.

---

# 220. Deletion/Archive Race

Foreign reference قد تصبح غير صالحة بعد validation.

لو هذا غير مقبول Business-wise، يحتاج lifecycle/event/ownership design، لا cross-module FK عشوائية.

---

# 221. Provider Contract قد ترجع Status بدل Filter

حسب Use Case، أحيانًا الأفضل ترجع:

```text
Status = Active/Suspended/...
```

وتترك Consumer تقرر.

وأحيانًا Contract مختصة ترجع فقط eligible item.

الاختيار يوثق semantics.

---

# 222. لا Double Business Rule

لو معنى `EmailVerified` ملك IdentityAccess، Institutions لا تعيد اشتقاقها من Columns أو تواريخ داخلية.

تستخدم fact الرسمية.

---

# 223. Derived Facts

Provider يمكنها حساب derived fact إذا مشتقة بالكامل من state التي تملكها.

---

# 224. Cross-owned Derived Decision ممنوعة

لو derived decision تعتمد على facts من Modules متعددة، لا تنسبها Provider واحدة كSource of Truth بلا ownership واضحة.

---

# 225. Use Case Start — Security First

في authenticated institution flow:

```text
T13 authenticated actor
↓
T14 institution context
↓
required permission
↓
other module facts عند الحاجة
↓
business rule
↓
local persistence
```

حسب الـUse Case.

---

# 226. Ordering لا تكون واحدة لكل Use Cases

بعض bootstrap flows مثل building institution context تحتاج Account Facts أثناء resolver نفسها.

الترتيب يُوثق في feature، مع الحفاظ على fail-closed.

---

# 227. No Security Side Effect من Read Contract

Account facts read لا تغير SecurityVersion ولا sessions.

---

# 228. S03-T04 Account Eligibility Contract

Sprint 03 تعتمد T18 مباشرة في Task الخاصة بـAccount Facts/Eligibility Contract.

المطلوب معماريًا:

```text
IdentityAccess public read contract
+
Internal implementation
+
Institutions one-way reference
+
No DbContext/entity leakage
```

---

# 229. S03-T06 Institution Access Resolver

Resolver في Institutions قد تستخدم Account Facts contract للتحقق من:

- verification/status facts المطلوبة صراحة للـUse Case ولم تضمنها T13 بالفعل.
- أي IdentityAccess-owned state مطلوبة حسب T14/Sprint 03.

ثم تكمل Institutions-owned membership checks.

لا يوجد `AccountMode`, `GeneralMode` أو `StudentMode` في هذا المسار أو في أي Contract طبقًا لـT12–T14.

---

# 230. Resolver لا تصبح Cross-Module God Service

`InstitutionAccessResolver` تظل مسؤولة عن institution access فقط.

لا تدير:

- sessions.
- password.
- subscriptions.
- academic rules.

---

# 231. S03-T08 My Institutions

Use Case قد تستخدم:

```text
ICurrentActor
+
Account Facts
+
InstitutionsDbContext
```

لكن لا query IdentityAccess tables.

`Account Facts` هنا optional حسب احتياج الـUse Case؛ لا تستدعى لمجرد وجود Layer أو لأن المستخدم authenticated.

---

# 232. S03-T09 Branch Query

بعد validated `IInstitutionContext`، branch query تعتمد أساسًا على Institutions-owned state.

لا cross-module call غير ضرورية.

---

# 233. No Redundant Call لمجرد الـLayering

إذا Fact موجودة authoritative داخل نفس module/context الحالية، لا نستدعي Module أخرى لمجرد اتباع pattern.

---

# 234. IdentityAccess Contract لا ترجع Membership

Membership ملك Institutions.

ممنوع IdentityAccess `AccountFacts` تحتوي:

```text
InstitutionRole
BranchScope
Capabilities
```

---

# 235. Institutions Contract لا ترجع Password/Auth secrets

Ownership boundaries ثنائية الاتجاه.

---

# 236. Future Academic → Institutions

عند إنشاء StudyGroup مستقبلًا، Academic قد تحتاج Contracts من Institutions لتأكيد:

- current validated institution.
- branch existence/belonging/usability.
- authorization.

لكن لا تستخدم `InstitutionsDbContext`.

---

# 237. Future Students/Enrolments

نفس النمط يستمر:

```text
Consumer domain
→ provider-owned narrow facts
```

بدون shared entities.

---

# 238. Future Subscriptions

Institution/Subscription workflows تحتاج مراجعة خاصة لأن trial/subscription changes cross ownership.

T18 وحدها لا تمنح Atomicity.

---

# 239. Create Institution + Trial

لو إنشاء Institution الحقيقي يحتاج:

```text
Institutions state
+
Subscriptions Trial state
```

لا يوجد لها shared-transaction exception معتمدة حاليًا؛ لذلك ممنوع قياسها على `RecordCashPayment` أو استخدام Shared DbContexts مباشرة.

لازم Business 12 + T19/ownership workflow تحسم التصميم، أو يصدر مستقبلًا named exception جديدة بتعديل صريح في T15/T18 بعد إثبات الحاجة. لا توجد موافقة ضمنية.

---

# 240. Synchronous Trial Check فقط لا يحل Workflow

مجرد سؤال:

```text
IsTrialEligible?
```

ثم إنشاء institution لا يمنع race/partial state إذا trial reservation نفسها يجب أن تكون atomic عبر Modules.

---

# 241. Cross-Module Reservation

لو Business تحتاج reservation/claim في Provider، هذه Command مستقلة لها local transaction/idempotency semantics، ولا يجب تصميمها بدون workflow كامل.

---

# 242. No Premature Contracts

لا ننشئ Contracts لكل Module المستقبلية الآن.

نعرف Contract عند أول Use Case حقيقية تحتاجها.

---

# 243. YAGNI على Public Surface

كل public type تزيد coupling cost.

نضيف أقل surface ممكنة.

---

# 244. Contract Review Checklist — Ownership

قبل إضافة Contract:

- من يملك الحقيقة؟
- هل Consumer فعلًا تحتاج current fact؟
- هل القرار Business للConsumer أو Provider؟
- هل هناك حل محلي أبسط؟

---

# 245. Contract Review Checklist — Architecture

- direction واحدة؟
- لا cycle؟
- لا BuildingBlocks business leakage؟
- لا DbContext/Entity exposure؟
- no internal HTTP؟

---

# 246. Contract Review Checklist — Data

- minimal fields؟
- no unnecessary PII؟
- no secrets؟
- immutable؟
- no lazy query؟

---

# 247. Contract Review Checklist — Correctness

- expected negative semantics واضحة؟
- infra failures لا تُخفى؟
- cancellation passes؟
- TOCTOU understood؟
- no fake cross-module atomicity؟

---

# 248. Contract Review Checklist — Performance

- no N+1؟
- bounded request؟
- indexed provider query؟
- no excessive call chain؟

---

# 249. Contract Review Checklist — Security

- trusted actor/context source؟
- no client authority؟
- no tenant leak؟
- no authz state in JWT؟
- no replay of stale security cache؟

---

# 250. Definition of Done لأي Synchronous Read Contract

يعتبر Contract جاهزة عندما:

- Provider ownership موثقة.
- Consumer/Provider reference direction واضحة.
- input/result minimal.
- expected outcomes موثقة.
- CancellationToken موجودة.
- implementation internal.
- DI registration provider-owned.
- no EF/HTTP/domain entity leakage.
- unit tests موجودة.
- integration test للcritical flow موجودة.
- architecture dependency test/inspection green.

---

# 251. Definition of Done لأي Synchronous Write Contract

بالإضافة لما سبق:

- لماذا synchronous write مطلوبة موثق.
- provider local transaction موثقة.
- caller local transaction ليست مشتركة في ordinary command contract.
- partial success semantics واضحة.
- retry/idempotency rules واضحة.
- `OperationId` + uniqueness + reconciliation موجودة عندما duplicate effect أو Commit ambiguity مؤذية.
- no external side effects inside open transaction.
- no exactly-once claim.
- T19 alternative تمت مراجعتها.

## 251.1 Definition of Done لأي Named Atomic Participant Contract

بالإضافة لما سبق:

- الاستثناء مسمى ومعتمد صراحة في T15 وT18؛ حاليًا `RecordCashPayment` فقط.
- orchestrator واحدة تملك Begin/Commit/Rollback للـatomic scope.
- كل participant تملك DbContext/locks/writes الخاصة بها فقط.
- لا raw `DbContext` أو Connection/Transaction تظهر للـbusiness orchestrator.
- الـapproved enlistment handle لا تستخدم خارج الـfeature.
- idempotency claim تسبق business locks طبقًا لـT16/T17.
- lock ordering وtimeouts موثقة.
- failure بين أي خطوتين يثبت Rollback الكامل على PostgreSQL الحقيقية.
- commit ambiguity تختبر وتتصالح بنفس `OperationId`.
- Architecture test تمنع أي caller أو participant غير معتمدة من استخدام الـatomic scope.

---

# 252. Contract Testing لا تعتمد على Implementation Type

Consumer tests تعتمد interface/contract behavior، لا concrete internal provider type.

---

# 253. Integration DI Test

نعمل Test يثبت إن Composition Root تسجل provider contract وأن Consumer resolve تنجح بدون reflection/service locator hacks.

---

# 254. Missing Registration

لو Provider contract المطلوبة غير مسجلة:

startup/test يجب أن يفشل بوضوح، لا runtime null fallback.

---

# 255. No Optional Null Service

لا pattern مثل:

```text
IAccountFactsReader? reader
```

ثم skip validation لو service مفقودة.

Critical dependency required.

---

# 256. No Fallback Local Copy

لو Provider غير متاحة بسبب configuration/bug، Consumer لا تستخدم cached hardcoded/default facts كfallback أمني.

---

# 257. Module Disabled/Not Implemented

لو Module dependency لم تُنفذ بعد، Feature التي تحتاجها لا تعتبر Ready.

لا fake implementation في Production.

---

# 258. Testing Fakes

Test projects يمكنها fake Contract behavior بشكل صريح.

لكن Production DI لا تستخدم fake fallback.

---

# 259. Environment-specific Provider

أي alternate implementation في Testing/Development يجب أن تكون واضحة ومقيدة بالبيئة ولا تغير Production semantics، مثل قواعد T12 للOTP.

---

# 260. No Contract Data Mutation after Return

Collections returned تكون read-only/immutable قدر الإمكان لمنع accidental mutation semantics.

---

# 261. Equality/Identity

Contract DTO equality لا تستخدم كبديل لDomain identity/concurrency logic.

---

# 262. Version Fields

لا نخرج Entity `Version` للConsumer إلا لو Use Case cross-module محددة تحتاج expected-version semantics، وده استثناء يحتاج T16 review.

---

# 263. SecurityVersion

`SecurityVersion` لا تخرج كcross-module concurrency token.

T13 concern فقط.

---

# 264. Provider Database Constraints

Provider تظل مسؤولة عن constraints الخاصة بها.

Consumer pre-check لا تستبدل Provider DB constraints.

---

# 265. Consumer Database Constraints

Consumer تظل مسؤولة عن uniqueness/invariants المحلية حتى لو provider fact validated.

---

# 266. No Cross-Module FK Verification at DB Layer

T18 لا تضيف database foreign keys عبر schemas.

---

# 267. Recovery/Reconciliation

لو cross-module references يمكن أن تصبح stale، reconciliation/event lifecycle قد تحتاج لاحقًا.

T18 لا تخفي هذا الاحتمال.

---

# 268. Failure Ordering

الـConsumer تفضل تنفيذ cheap/local validation قبل expensive provider DB call عندما لا يؤثر ذلك على security/error semantics.

---

# 269. Security Ordering

لكن لا نستخدم validation ordering لتسريب existence أو bypass authorization.

T14/T32 تظل الحاكمة.

---

# 270. Query Result Freshness

Synchronous query تعني read لحظة تنفيذ Provider query، لا guarantee أن value ستظل ثابتة لباقي request بعد العودة.

---

# 271. No Snapshot Isolation Across Modules

لا يوجد global consistent snapshot بين DbContexts/Modules في T18.

---

# 272. Read Committed Reality

Provider queries تعمل وفق transaction/isolation المحلية حسب T15.

Consumer لا تفترض global snapshot.

---

# 273. High-risk Foreign Fact

لو قرار مالي/أمني حساس يعتمد Fact يمكن أن تتغير بسرعة، يجب توثيق race model واختيار workflow مناسب؛ مجرد sync query قد لا تكفي.

---

# 274. Authorization Mutable Server-side

Membership/role/scope changes في Institutions تؤثر على request التالية بدون انتظار JWT، وفق T14.

T18 لا تضيف stale authorization replication.

---

# 275. Identity Facts Mutable Server-side

Account verification/status facts تُقرأ من IdentityAccess عند الحاجة؛ لا نثق في stale client data.

---

# 276. No Client-supplied Contract DTO

لا يقبل Endpoint DTO ويحولها مباشرة إلى `AccountFacts` ويعتبرها trusted.

Provider هي التي تنتج Facts.

---

# 277. No Contract Spoofing

Contract objects server-created فقط داخل runtime path.

---

# 278. Testing Authorization Change

Integration test مهمة تثبت إن تغيير Account/Membership state في Provider يغير قرار Consumer في request التالية حيث contract تعتمد current facts.

---

# 279. Parallel Consumers

لو عدة Modules تستدعي نفس Provider contract في requests متوازية، implementation يجب أن تكون stateless request-scoped أو آمنة حسب DI lifetime.

---

# 280. No Static Mutable Contract State

ممنوع static caches/holders للأfacts بلا T29 decision.

---

# 281. No Cross-request Mutable State

Scoped implementation لا تترك data في singleton/static تؤثر على طلب آخر.

---

# 282. DbContext Pooling

أي Module تعتمد tenant/request state في DbContext تتبع T09؛ لا نغير pooling policy من T18.

---

# 283. Provider Query Filters

Provider تطبق query filters/security rules الخاصة ببياناتها.

Consumer لا bypass.

---

# 284. Global Data Contracts

IdentityAccess AccountFacts ليست tenant-scoped لأنها account-level facts، لكن consumer institution flow تظل tenant-authorized في Institutions.

---

# 285. Multi-Tenant Data Contracts

Institutions contracts التي تعبر tenant data يجب أن تكون context-aware/fail-closed حسب T09/T14.

---

# 286. Branch Facts

لو Module مستقبلًا تحتاج branch facts، Provider `Institutions` ترجع minimal branch facts، لا Branch Entity.

---

# 287. Capability/Permission Facts

لا نخرج effective permission dump لكل Consumer بلا حاجة.

نفضل authorization contract/evaluator عند الحاجة، حسب T14.

---

# 288. Permission Code Contract

Stable permission codes ملك Institutions Authorization model، وليست BuildingBlocks.

---

# 289. Provider Contract Security Surface

كون interface public بين assemblies لا يجعلها untrusted external endpoint؛ لكنها تظل security-sensitive ويجب أن تكون narrow.

---

# 290. No Module Identity Spoofing

لا نحاول بناء caller-module authentication داخل نفس process في v1.

الثقة مبنية على compile-time architecture boundaries + code review/tests، وليس API keys بين Modules.

---

# 291. Future Service Extraction

إذا Module انفصلت مستقبلًا لخدمة مستقلة:

- نحتفظ semantic contract.
- transport/error/retry/auth يعاد تصميمهم.
- لا نفترض إن internal interface يمكن تحويلها حرفيًا إلى remote API.

---

# 292. No Premature Remote-friendly Abstraction

لا نضيف serialization envelopes/network DTOs/circuit breakers الآن فقط لاحتمال microservices مستقبلًا.

---

# 293. Contract and Source-of-Truth

Contract هي access boundary للSource of Truth، وليست نسخة مستقلة منه.

---

# 294. Consumer Snapshot Naming

لو Consumer تخزن value من Provider تاريخيًا، الاسم يوضح:

```text
...Snapshot
...AtCreation
...AtIssue
```

---

# 295. No Silent Synchronization

لا background sync لمعلومات cross-module دون قرار T19/T21.

---

# 296. No Event + Sync Double Source

لو Module عندها local projection من events مستقبلًا، يجب تحديد هل Use Case تستخدم projection أم authoritative sync query؛ لا نخلط الاثنين عشوائيًا.

---

# 297. Security-critical Source

Authorization/security decision تستخدم المصدر الذي يحدده T13/T14/T35، لا faster stale copy لمجرد الأداء.

---

# 298. Public Contract Review في PR

أي زيادة public member في `Contracts/` تعامل كArchitecture change صغيرة وتراجع:

- ownership.
- consumers.
- privacy.
- lifecycle.

---

# 299. Contract Deprecation

لو Contract لم تعد مطلوبة:

- ننقل Consumers أولًا.
- نحذفها من Provider في نفس repository change المناسب.
- لا نترك legacy contract دائمة بلا مستخدم.

---

# 300. No Compatibility Layer بلا Consumer

لا نحافظ على method aliases/DTO versions قديمة لمجرد الخوف من break داخل monorepo.

---

# 301. CancellationToken في Examples

الشكل المرجعي:

```csharp
public interface IAccountFactsReader
{
    Task<AccountFactsReadResult> GetAsync(
        Guid userAccountId,
        CancellationToken cancellationToken);
}
```

`AccountFactsReadResult` تكون typed outcome مغلقة تميز `Found(AccountFacts)` عن `NotFound`. Cancellation وInfrastructure failures لا تمثلان كـNotFound؛ تتبعان القسم 50.1. الشكل النهائي قد يختلف في naming، لكن هذه semantics ثابتة.

---

# 302. AccountFacts Example Shape

مثال مفاهيمي فقط:

```csharp
public abstract record AccountFactsReadResult
{
    public sealed record Found(AccountFacts Facts)
        : AccountFactsReadResult;

    public sealed record NotFound
        : AccountFactsReadResult;
}

public sealed record AccountFacts(
    Guid UserAccountId,
    AccountStatusCode Status,
    bool IsPhoneVerified,
    bool IsEmailVerified);
```

ده Design illustration، وليس إلزامًا بأسماء types لو T12 الحالية تستخدم naming مختلفة.

---

# 303. Provider Implementation Example

```csharp
internal sealed class AccountFactsReader(
    IdentityAccessDbContext dbContext)
    : IAccountFactsReader
{
    public async Task<AccountFactsReadResult> GetAsync(
        Guid userAccountId,
        CancellationToken cancellationToken)
    {
        // Provider-owned query only.
        // Minimal projection.
        // No tracked entity leaks.
        // No session side effects.
    }
}
```

---

# 304. Consumer Example

```csharp
internal sealed class SomeInstitutionsHandler(
    ICurrentActor currentActor,
    IAccountFactsReader accountFactsReader,
    InstitutionsDbContext dbContext)
{
    // read trusted actor
    // query provider facts
    // apply Institutions-owned rules
    // persist Institutions-owned state only
}
```

---

# 305. Anti-pattern — Foreign DbContext

```csharp
internal sealed class Handler(
    InstitutionsDbContext institutionsDb,
    IdentityAccessDbContext identityDb)
```

```text
❌ ممنوع
```

---

# 306. Anti-pattern — Foreign Entity

```csharp
public UserAccount Account { get; set; }
```

داخل Institutions entity:

```text
❌ ممنوع
```

---

# 307. Anti-pattern — IQueryable Contract

```csharp
IQueryable<UserAccount> QueryAccounts();
```

```text
❌ ممنوع
```

---

# 308. Anti-pattern — General Service

```csharp
IIdentityAccessService
```

بـdozens methods:

```text
❌ ممنوع
```

---

# 309. Anti-pattern — API Self-call

```text
HttpClient → localhost/api/v1/...
```

```text
❌ ممنوع
```

---

# 310. Anti-pattern — Unapproved Shared Transaction

```text
IdentityAccessDbContext
+
InstitutionsDbContext
+
one DbTransaction
```

```text
❌ ممنوع كتصميم عام أو بدون named T15 exception
```

هذا المثال لا يصف `RecordCashPayment` المعتمدة: هناك لا تحقن Module DbContext أجنبية، بل تستدعي orchestrator participants مملوكة لموديولاتها داخل atomic scope مقيدة طبقًا للقسم 89.1.

---

# 311. Anti-pattern — Circular Ref

```text
Institutions → IdentityAccess
IdentityAccess → Institutions
```

```text
❌ ممنوع
```

---

# 312. Anti-pattern — InternalsVisibleTo Business Module

```text
[assembly: InternalsVisibleTo("EduCenterOS.Modules.Institutions")]
```

لاختراق IdentityAccess internals:

```text
❌ ممنوع
```

---

# 313. Anti-pattern — Raw SQL Cross-schema

```sql
SELECT ... FROM identity_access.user_accounts
```

من Institutions:

```text
❌ ممنوع
```

---

# 314. Anti-pattern — Cross-module Lock

```text
Provider locks row
→ Consumer keeps provider lock open
→ Consumer commits own DB
```

```text
❌ ممنوع
```

---

# 315. Anti-pattern — Hide DB Outage as 404

```text
catch Exception
→ return NotFound
```

في provider contract:

```text
❌ ممنوع
```

---

# 316. Anti-pattern — Consumer-calculated Provider Fact

Institutions تعيد حساب Email verification من IdentityAccess internals:

```text
❌ ممنوع
```

---

# 317. Anti-pattern — Cross-module Business Decision

IdentityAccess ترجع:

```text
CanCreateInstitution
```

رغم إن القرار يعتمد Modules أخرى:

```text
❌ ممنوع
```

---

# 318. Anti-pattern — N+1

```text
foreach row
→ one provider call
```

```text
❌ ممنوع للLists الكبيرة
```

---

# 319. Anti-pattern — Cached Security Fact كSource of Truth

```text
cache role/account eligibility for entire JWT lifetime
```

بدون T29/T35 decision:

```text
❌ ممنوع
```

---

# 320. Anti-pattern — Client-provided Facts

Client ترجع:

```json
{
  "accountStatus": "Active",
  "emailVerified": true
}
```

والBackend تثق فيها:

```text
❌ ممنوع
```

---

# 321. Anti-pattern — Transaction Around Module Call

```text
Begin Institutions transaction
→ call IdentityAccess DB query
→ continue holding locks
```

كـdefault:

```text
❌ ممنوع
```

---

# 322. Anti-pattern — Remote Patterns داخل Monolith

```text
circuit breaker
HTTP retry
service discovery
API key between modules
```

لمجرد التواصل الداخلي:

```text
❌ غير معتمد
```

---

# 323. القرار لـSprint 03

قبل تنفيذ `S03-T04/S03-T06` نطبق T18 كالتالي:

```text
IdentityAccess exposes one narrow public Account Facts read contract
↓
Institutions references IdentityAccess project one-way
↓
Institutions injects contract through DI
↓
No IdentityAccessDbContext/Entity access
↓
No reverse IdentityAccess→Institutions reference
```

---

# 324. Account Facts Contract DoD لـSprint 03

يجب أن تثبت:

- public interface داخل `IdentityAccess.Contracts`.
- public immutable contract types فقط.
- internal provider implementation.
- scoped DI.
- minimal SQL projection.
- no tracking عند القراءة إذا مناسب.
- no PII/secrets.
- cancellation.
- unit tests.
- integration test مع PostgreSQL.
- architecture test للdependency direction.

---

# 325. Sprint 03 Security DoD

- account state change تؤثر على request التالية حسب use case.
- no JWT permission/account-status/verification shortcut.
- no cross-tenant leak.
- no session sliding side effect.
- no provider DB outage mapped كـvalid access.

---

# 326. ما لا يحسمه T18

T18 لا يحسم:

- Event type catalog.
- Domain Event dispatcher implementation.
- Outbox schema/dispatcher.
- Inbox/deduplication.
- Message broker.
- Background job framework.
- remote service transport.
- caching TTLs.
- circuit breakers للخدمات الخارجية.
- API gateway.
- observability implementation.
- distributed tracing details.
- payment provider integration.
- notification transport.
- service extraction strategy.
- exact Create Institution + Trial orchestration.
- per-workflow table/schema details الخاصة بحفظ internal OperationId؛ القواعد العامة محسومة في القسم 170.

---

# 327. الحاجات الممنوعة بوضوح

```text
❌ Module تستخدم DbContext Module أخرى
❌ Module تستخدم Entity/Repository داخلية من Module أخرى
❌ Cross-module IQueryable/DbSet/ChangeTracker
❌ Cross-schema raw SQL من Business Module أخرى
❌ Cross-module Foreign Keys/Navigation Properties
❌ internal HTTP/gRPC داخل نفس host كـdefault
❌ generic Module Bus
❌ Service Locator
❌ reflection/dynamic dispatch
❌ MediatR كـcross-module bus في v1
❌ Circular Module references
❌ InternalsVisibleTo بين Business Modules
❌ Business contracts داخل BuildingBlocks
❌ Shared Business Contracts God project
❌ public Domain Entities لمجرد reuse
❌ client-provided facts كauthority
❌ PII/secrets في facts بلا حاجة
❌ authz state في JWT لتجنب module calls
❌ stale security cache كSource of Truth
❌ unapproved shared cross-module DbTransaction/TransactionScope
❌ foreign cross-module row ownership/locks
❌ module call داخل local transaction كـdefault خارج named atomic participant
❌ exactly-once claim للcross-module command
❌ raw HTTP Idempotency-Key كinternal contract تلقائيًا
❌ N+1 cross-module calls
❌ infrastructure failure disguised as NotFound/Denied
❌ API endpoint تنظم Business orchestration بين Modules
❌ fake Production fallback لو provider contract missing
```

---

# 328. الحاجات المعتمدة بوضوح

```text
✅ in-process typed contracts
✅ Provider-owned Contracts
✅ one-way Consumer→Provider Project Reference
✅ Contracts folder داخل Module Project نفسها
✅ internal-by-default implementation/domain
✅ narrow public interface/DTO/code surface
✅ direct constructor injection
✅ synchronous authoritative facts عند الحاجة
✅ Consumer-owned business decision
✅ provider-owned persistence/rules
✅ minimal immutable results
✅ no EF/HTTP leakage
✅ async + CancellationToken
✅ scoped provider implementation عند DbContext usage
✅ provider-owned DI registration
✅ local transaction per Module by default
✅ RecordCashPayment is the only named atomic-participant exception
✅ each atomic participant owns only its DbContext/locks/writes
✅ TOCTOU acknowledged explicitly
✅ bulk bounded contract بدل N+1 عند الحاجة
✅ server-derived actor/tenant context
✅ T13/T14 security remains authoritative
✅ T15/T16/T17 semantics remain authoritative
✅ T19 for asynchronous durable follow-up
✅ real integration tests + architecture tests
✅ Sprint 03 Institutions→IdentityAccess Account Facts contract
```

---

# 329. Definition of Done لـT18

يعتبر T18 مقفولًا عندما أصبح واضحًا:

- لماذا نحتاج synchronous module communication.
- من يملك Contract.
- مكان Contract.
- public/internal rules.
- Project reference direction.
- circular dependency policy.
- DI pattern.
- query vs command policy.
- facts vs business decisions.
- DTO/privacy rules.
- no DbContext/entity/query leakage.
- error semantics.
- fail-closed vs infrastructure failure.
- async/cancellation/lifetimes.
- transaction/TOCTOU behavior.
- no unapproved cross-module atomicity claim.
- RecordCashPayment atomic-participant exception وحدودها.
- typed expected outcomes مقابل cancellation/infrastructure exceptions.
- OperationId/reconciliation للcross-module commands المادية.
- no internal HTTP/Module Bus.
- performance/N+1 rules.
- tenant/security rules.
- testing/architecture checks.
- Sprint 03 Account Facts direction.
- boundaries with T19/T20/T28/T29/T34/T35.

---

# 330. القرار النهائي المختصر

> يعتمد EduCenterOS التواصل المتزامن بين الـBusiness Modules كاستدعاءات Typed In-Process Contracts مباشرة عبر Dependency Injection، وليس عبر HTTP/gRPC أو Message Broker أو Generic Module Bus داخل الـModular Monolith. الـProvider Module التي تملك الحقيقة هي التي تملك الـContract العامة داخل `Contracts/` في نفس Module Project، بينما implementation والـDomain والـInfrastructure تظل `internal` افتراضيًا. الـConsumer تضيف Project Reference اتجاه واحد للـProvider وتستخدم الـContracts فقط؛ Circular References و`InternalsVisibleTo` بين Business Modules وShared Business Contracts داخل BuildingBlocks ممنوعة.
>
> الاستخدام المفضل هو Synchronous Read/Facts Contracts عندما تحتاج Use Case إلى current authoritative fact من Module أخرى. الـProvider ترجع Minimal Immutable Facts التي تملك معناها فقط، ولا ترجع Entities أو DbContext أو Repository أو `IQueryable` أو HTTP/EF/Npgsql types أو Secrets/PII بلا ضرورة. الـConsumer تظل مالكة لBusiness decision الخاصة بها؛ فـIdentityAccess مثلًا يمكنها أن ترجع Account status وPhone/Email verification facts، لكنها لا تقرر `CanCreateInstitution` إذا القرار يعتمد Institutions/Subscriptions. لا يوجد `AccountMode` أو `GeneralMode` أو `StudentMode`. الـcurrent actor والtenant context والauthorization تظل server-derived وفق T13/T14، ولا نستخدم JWT أو client round-trip كSource of Truth.
>
> كل I/O contract تكون Async وتمرر `CancellationToken`، والimplementation التي تعتمد على DbContext تسجل Scoped داخل Provider registration. لا Service Locator أو reflection أو hidden runtime dispatch، ولا N+1 calls؛ إذا احتجنا collection حقيقية نعرف bounded bulk contract أو pagination. Provider infrastructure failures لا تُخفى كNotFound/Denied، وfail-closed يعني عدم منح صلاحية عند failure مع بقاء الخطأ Infrastructure sanitized وفق T32.
>
> T18 لا تنشئ Atomicity عامة عبر Modules. قراءة Fact من Provider ثم Commit في Consumer معرضة بطبيعتها لـTOCTOU؛ لو invariant لا تتحمل تغير الـFact بين التحقق والاستخدام، نراجع ownership أو نصمم workflow متعددة الخطوات، ومع الحاجة إلى durable follow-up/eventual consistency نستخدم T19 Events/Outbox. الاستثناء الوحيد المعتمد هو `RecordCashPayment`: feature-specific orchestrator وopaque atomic scope وparticipants مملوكة لموديولاتها طبقًا لـT15–T17؛ لا يسمح بالقياس عليها دون قرار جديد. Ordinary synchronous commands تملك Provider-local transaction، والعمليات المادية تستخدم Stable OperationId وprovider uniqueness/reconciliation بدل exactly-once delivery claim.
>
> في Sprint 03 الاتجاه المعتمد هو `Institutions → IdentityAccess.Contracts` من خلال Account Facts read contract محدودة، بدون أي reference عكسية من IdentityAccess إلى Institutions وبدون الوصول إلى `IdentityAccessDbContext` أو `UserAccount` Entity. Contract تخرج فقط Account status وverification facts المطلوبة فعلًا ولم تضمنها T13 بالفعل، وتظل membership/role/scope/capabilities ملك Institutions. هذا boundary يجب إثباته بوحدات اختبار للConsumer/Provider، Integration Tests على PostgreSQL للحالات الحرجة، وArchitecture Tests تمنع DbContext/entity/circular/internal-HTTP leakage.
