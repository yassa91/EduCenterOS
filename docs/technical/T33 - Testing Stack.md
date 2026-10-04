# T33 - Testing Stack

> **2026-10-04 — S03 scope clarification:** The approved [S03](../sprints/S03.md) implements Authentication & Session Management. Earlier Sprint 03 / S03-T04 / S03-T06 Institutions/Account Facts examples below describe future institution work, not the current task numbering or authorization. Their module-boundary rules remain applicable when that feature is implemented.

> **2026-10-03 — Current development/testing override:** [T41 — Supabase Development & Testing](T41%20-%20Supabase%20Development%20%26%20Testing.md) replaces local PostgreSQL/Docker/Testcontainers provisioning and the synthetic-only infrastructure credential rule. The owner requested two cloud projects and an empty development database. The following earlier decision/evidence remains historical where it conflicts with T41. Remote compatibility verification is required before DB01 is complete.

## الهدف من القرار

تثبيت Testing Stack وسياسة اختبارات قابلة للتنفيذ داخل EduCenterOS، بحيث تثبت Business/Security behavior وحدود الموديولات وعقود HTTP وPostgreSQL، وتكون معزولة وقابلة للتكرار محليًا وفي CI.

T33 تحدد **عقود الاختبار المطلوبة عند تنفيذ Feature**؛ لا تدعي وجود Test Projects أو test infrastructure منفذة بالفعل. السيناريوهات الخاصة بـFeature مستقبلية تدخل Definition of Done عند تنفيذها، ولا تفرض بناء Feature مبكرًا لمجرد اختبارها.

---

# القرار النهائي

```text
xUnit.net v3 framework generation + VSTest runner
+
UnitTests / IntegrationTests / ArchitectureTests
+
WebApplicationFactory<Program> + TestServer
+
Real PostgreSQL 18
+
One isolated test database per run/worker, schema per active module
+
Explicit fail-closed database ownership guard
+
Testing host with production registration and narrow adapter overrides
+
Runtime-generated security material
+
IClock/TestClock and precision-aware time boundaries
+
Serial integration suites + controlled concurrency inside each race test
+
Fresh migrations + explicit dependency order
+
Complete T31/T32 contract coverage and T13–T18 risk matrices
+
No retry-until-green, no required critical skips
+
Behavior/risk coverage and reviewable verification evidence
```

---

# 1. المرجعية وحدود القرار

- T01–T18 تحدد التكنولوجيا والـArchitecture والـDomain والـIdentity والـAuthorization والـTransactions والـConcurrency والـIdempotency والـModule Contracts.
- T31/T32 تحددان عقود API/Errors؛ T33 تثبتها ولا تغير معناها.
- T34/T35 تحددان Observability/Security engineering التفصيلية، وT37/T38/T39 تحدد Docker/CI/Hosting.
- الاختبارات executable specification للقواعد المعتمدة. Requirement غير محسومة تسجل كopen decision؛ لا نخترع expectation ثم نعاملها كBusiness Rule.
- المشروع في مرحلة تثبيت القرارات قبل بدء التنفيذ. الملحق A يجمع أمثلة لتطبيق سياسة الاختبار عند تنفيذ الـFeatures المعنية؛ لا يحدد خطة عمل أو ترتيب تنفيذ أو Tasks مرقمة.

---

# 2. Framework وRunner Profile

نعتمد **جيل xUnit.net v3** باستخدام stable `xunit.v3` package family، و**VSTest execution mode** عبر `dotnet test` لكل المشاريع الثلاثة.

| Dependency | Ownership / purpose |
|---|---|
| `xunit.v3` | Test framework؛ اسم الجيل مستقل عن رقم إصدار الـpackage |
| `xunit.runner.visualstudio` | VSTest adapter متوافقة مع xUnit v3 |
| `Microsoft.NET.Test.Sdk` | VSTest discovery/execution |
| `Microsoft.AspNetCore.Mvc.Testing` | IntegrationTests؛ ASP.NET Core major 10 وفق T01 |
| Npgsql / EF Core provider | PostgreSQL integration؛ versions متوافقة مع T01/T07 |
| `dotnet-ef` عند استخدام CLI | Local tool manifest، version متوافقة مع EF Core 10 |

القواعد:

- Target framework هي `net10.0`، وSDK مضبوطة في `global.json` وفق T01.
- Stable versions المتوافقة تثبت مركزيًا في `Directory.Packages.props`؛ لا latest-floating أو Preview/RC ولا نسخ متفرقة.
- VSTest هو runner profile المعتمد. لا template/CI override تحول `dotnet test` إلى Microsoft.Testing.Platform ولا تفعّل `TestingPlatformDotnetTestSupport` لإعادة توجيهه إلى MTP.
- وجود dependency transitive لا يعني تغيير runner؛ ممنوع خلط VSTest execution وMTP execution بين Test Projects.
- Scaffold تختار VSTest صراحة إذا template تعرض أكثر من profile. Smoke تثبت discovery والتنفيذ والfilter والreporting لكل Project على SDK المثبتة.
- تغيير runner لاحقًا يحتاج تحديث packages/configuration/commands/reporting معًا واختبار compatibility.
- `xUnit Assert.*` هي default assertions. Moq/NSubstitute/FakeItEasy وFluentAssertions وAutoFixture وSnapshot/Approval frameworks ليست baseline؛ إضافتها تحتاج حاجة فعلية موثقة.
- Test packages لا تصبح Production dependencies لمجرد سهولة الاختبار.

---

# 3. Test Projects والتنظيم

```text
tests/
├── EduCenterOS.UnitTests/
├── EduCenterOS.IntegrationTests/
└── EduCenterOS.ArchitectureTests/
```

- المشاريع لا reference لبعضها. Shared helper project لا ينشأ إلا لتكرار حقيقي، ولا يحمل Business semantics.
- كل Test Project تضع direct production references اللازمة فقط؛ ArchitectureTests يمكنها فحص جميع الموديولات دون منح Production references جديدة.
- التنظيم Vertical Slice حسب Module/Feature؛ cross-cutting HTTP tests تحت area مثل `IntegrationTests/Api`، والمساعدات المشتركة فعلًا تحت `IntegrationTests/Infrastructure`.
- Feature-specific builders قرب اختبارات الـFeature. لا MagicFixture ضخمة أو General Test Framework مبكرة.
- `InternalsVisibleTo` مسموحة للـTest Assemblies عند الحاجة، ولا تفتح حدودًا بين Business Modules أو توسع public Production API.
- `SubjectTests` للـclasses و`MethodOrOperation_Scenario_ExpectedResult` للـmethods. Arrange/Act/Assert واضحة، والتعليقات اختيارية.
- فصل المشاريع كافٍ للتصنيف الأساسي؛ Traits اختيارية إذا احتاجتها T38/CI grouping.

---

# 4. اختيار مستوى الاختبار

| Level | Behavior proved | Prerequisites |
|---|---|---|
| Unit | Domain invariants، Value Objects، normalizers، pure permission evaluator، request validators، Error/Result factories، options validators | No DB / API host / network / Docker |
| Integration | HTTP pipeline، DI/registration، JWT/AuthZ، PostgreSQL projections/constraints/transactions/locks/migrations، module contracts | Host/DB بحسب السلوك الفعلي |
| Architecture | References/dependency direction/public surface/context shapes/forbidden access | Source/assemblies؛ no DB/network |
| Backend API E2E | HTTP → middleware/auth → production handler → PostgreSQL → HTTP | داخل IntegrationTests |
| Browser E2E | Browser cookie/CORS enforcement/UI workflows | مؤجلة حتى Frontend فعلية |

إذا Unit تثبت السلوك كاملًا نستخدمها؛ correctness المعتمدة على EF/PostgreSQL أو HTTP middleware تحتاج Integration. Handler unit مسموحة مع dependencies ضيقة عندما persistence ليست موضوع الاختبار.

EF Core InMemory وMock/Fake DbContext/DbSet لا يثبتان PostgreSQL behavior. SQLite ليست بديلًا افتراضيًا؛ أي استخدام محدود لها لا يقدم كدليل على PostgreSQL-specific correctness.

TestServer يثبت in-process ASP.NET pipeline؛ لا يثبت TLS/network/load أو browser enforcement أو reverse-proxy behavior. External sandbox/load/performance/penetration suites منفصلة عن normal gate وتحددها قراراتها.

---

# 5. PostgreSQL Ownership and Isolation

الـsystem/cross-module Integration suite تستخدم **PostgreSQL 18 حقيقية، Database واحدة معزولة لكل run/CI worker، وschema وDbContext وmigration history مستقلة لكل active Module** وفق T08.

```text
Isolated PostgreSQL instance/server
└── educenteros_<run-worker-id>_tests
    ├── identity_access + own migration history
    ├── institutions + own migration history
    └── other active module schemas
```

- كل Module DbContext في shared host تستخدم نفس test Database؛ اختلاف DbContext لا يعني Database per Module.
- Worker أخرى تحصل على Database/instance مختلفة. لا shared mutable test DB بين CI workers أو test processes.
- Feature-only persistence fixture قد تستخدم disposable DB أصغر، لكنها لا تغني عن system fixture التي تثبت schema coexistence والمigration dependencies وnamed transaction exception.
- Docker container أو Testcontainers for .NET مسموحتان. T37/T38 تحسم standard provisioning؛ contracts هنا لا تتوقف على library بعينها.
- PostgreSQL major 18 وrequired extensions/timeouts/provider semantics مماثلة للقرارات المعتمدة؛ minor patch تديرها البيئة.
- Infrastructure prerequisite المفقودة تؤدي لفشل واضح، لا silent skip أو fallback إلى developer connection string. Unit/Architecture تعمل مستقلتين عن DB.
- Test execution بعد restore لا تحتاج Internet أو external providers؛ initial package restore/provisioning قد يحتاج تنزيل dependencies.

---

# 6. Fail-Closed Database Safety Guard

قبل migration/reset/drop/truncate، يجب أن تحقق test infrastructure الشروط كلها:

1. Target connection صريحة من test configuration، دون User Secrets أو Production/Staging/developer business fallback.
2. Actual server/database identity من connection المفتوحة تطابق fixture-owned/explicitly allowlisted target. الاسم ينتهي بـ`_tests` كحماية إضافية؛ suffix وحدها لا تثبت الأمان.
3. Disposal/cleanup target لها ownership evidence: fixture أنشأتها لهذا run، أو pre-provisioned dedicated test DB مع test-only credentials وrun lease/ownership marker.
4. Migration/reset credentials scoped للبيئة المخصصة؛ لا Production credentials أو broad server-admin cleanup ضمن normal tests.
5. Cleanup manifest صريحة للـschemas/tables المطلوبة. Names تمر provider-safe identifier handling؛ لا SQL interpolation أو wildcard targets غير متحقق منها.

- Fresh empty DB تتحقق ملكيتها من provisioning metadata قبل وجود marker؛ marker لا تنشأ داخل target مجهولة لمجرد جعل guard تمر.
- Guard ترفض actual identity mismatch، connection missing، marker/lease invalid أو cleanup target خارج manifest قبل أي destructive action.
- Guard نفسها لها positive/negative Unit/Integration coverage، مع fake diagnostic values تمنع طباعة credentials.
- ممنوع DROP/TRUNCATE واسع على shared server أو DB غير مملوكة. Disposable database/container أفضل للـfull teardown.
- Cleanup failure تظهر كenvironment failure، ولا تختفي داخل finally أو تغطي original failure دون تسجيل الاثنين.

---

# 7. Fixture Lifecycle وParallelism

Lifecycle المعتمدة:

```text
Provision/claim isolated target → verify ownership → apply migrations
→ start test host → prepare/reset scenario → execute/assert
→ finish all in-flight work → reset/dispose → release/drop owned target
```

- `IClassFixture`/`ICollectionFixture` و`IAsyncLifetime` تستعمل حسب ownership الفعلية؛ لا dependence على test ordering.
- Core V1 IntegrationTests serial بين tests/classes بسبب shared database reset strategy:
  `[assembly: CollectionBehavior(DisableTestParallelization = true)]`.
- Unit/Architecture يمكن تشغيلهما بالتوازي طالما لا mutable global state أو locale/environment interference.
- In-test races/context-isolation requests متزامنة عمدًا رغم serial suite؛ كل competitor لها scope/DbContext/connection منفصلة.
- Reset per scenario يتم من manifest محددة بعد انتهاء requests/tasks؛ يحافظ على schema/migration histories، ويمسح business/idempotency/outbox state المعنية بترتيب يحترم FK dependencies. أي cascade/reset شامل يجب أن يظل داخل الـowned target والـmanifest المعتمدة.
- Mutable spies/senders/counters وTestClock تعاد تهيئتها لكل test. singleton/options/startup scenarios تستخدم fresh host عند الحاجة.
- لا outer rollback transaction حول HTTP test تعتمد نجاح Commit؛ requests تستخدم connections مختلفة، وwrapping قد يخفي commit/replay behavior.
- Background processing التي ليست موضوع السيناريو تتحكم فيها test composition صراحة؛ لا تعمل أثناء reset، ولا تتجاوز Business/Security registration المطلوبة.
- Environment variables/culture/process state عند تغييرها تحفظ وتستعاد، وتُسلسل الاختبارات المتأثرة. Mutable static state تتجنب.
- Dispose responses/connections/contexts/hosts/temp directories في ownership scope، حتى بعد failure. Cancellation/timeout يجب أن تنهي competitors قبل reset.

---

# 8. Migrations وSchema Verification

| Gate | Required proof |
|---|---|
| Normal persistence suite | جميع active module migrations مطبقة على isolated test DB قبل host/feature execution |
| Schema-sensitive Feature/Release closure | Fresh migration-from-zero على empty PostgreSQL 18 |
| Migration تغير existing data/constraints | Upgrade من prior supported migration state ببيانات صناعية، مع حفظ data/invariants حسب change |
| Model change | No pending model changes لكل affected DbContext، وfinal gate لجميع active contexts |

- Context inventory تسجل Module/schema/DbContext/migrations assembly/history table/connection configuration.
- Migrations تطبق بdependency order صريحة وفق T08، ومنها Structural FKs. لا circular ordering أو مشتركة history واحدة.
- Fresh gate لا تعتمد manual SQL repair أو `EnsureCreated`. Test seeding ليست migration repair.
- EF Core 10 pending-model check تستخدم supported API/CLI لكل context؛ لا تترك mandatory check اختيارية إذا متاحة في stack المثبتة.
- Upgrade coverage مطلوبة عند data-affecting migration؛ لا نفرض downgrade دعمًا لم تقرره deployment policy.
- Fixture هي التي تشغل setup migrations، وليس production startup behavior جديدًا. Production API لا تبدأ auto-migration وفق T08.
- يجوز reuse container/server للتكلفة؛ Fresh gate تنشئ Database فارغة مملوكة وتزيلها عند النهاية.

---

# 9. API Host وTest Configuration

- API path المفضل:
  `HttpClient → WebApplicationFactory<Program> → middleware → endpoint → production services/handler → PostgreSQL → response`.
- Ordinary integration host تستخدم `Testing` وتستدعي Production registration، ثم تستبدل adapters محددة من test composition.
- لا `if Testing => authorize everything` أو skip invariants/tenant guards أو registration أساسية متاحة فقط في Testing.
- Options/startup tests تشمل missing/invalid/contradictory/malformed configuration و`ValidateOnStart` فعليًا؛ service-provider Unit لا تغني عن Host test للسلوك host-specific.
- Production/Development registration scenarios تستخدم fresh hosts وtest-safe configuration؛ تثبت غياب Development-only OTP services/endpoints في Production.
- لا Developer User Secrets أو machine-specific paths/manual login/IDE/browser prerequisites للmandatory suites.
- Test overrides محددة في config manifest، ولا تغير unrelated Production defaults بلا سبب.
- HTTP client تستخدم HTTPS BaseAddress للحالات الأمنية، و`AllowAutoRedirect=false` افتراضيًا. `HandleCookies=false` عند فحص raw Set-Cookie/replay يدوي.
- Security middleware/transport tests تمر HTTP pipeline؛ direct Handler call لا تثبت CORS/JWT/CSRF/correlation/wiring.

---

# 10. Test Doubles وTesting Seams

- Default narrow hand-written fakes/stubs/spies: `TestClock`، `TestOtpSender`، test email/storage/provider adapters حسب abstractions الفعلية.
- لا SMS/Email/Payment/Webhook/File Storage production calls في normal suite. Sandbox suites مستقبلًا explicit opt-in/pipeline منفصلة.
- Test Auth scheme مسموحة فقط لFeature غير أمنية عندما Authentication ليست موضوعها، مع إثبات مستقل للreal auth path وعدم إخفاء tenant/access invariants.
- JWT/security tests تستخدم production issuance/validation بمفاتيح runtime-generated؛ malformed/wrong-signature tokens تصنع داخل test memory لاختبار الرفض.
- Production seam يجب أن تكون abstraction حقيقية أو typed observer/interceptor ضيقة. لا نضيف public endpoint أو public Business method لمجرد راحة test.
- Exception probe إن احتاجتها API writer tests تسجل داخل test host فقط وتختبر غيابها Production؛ exception صناعية دون secrets.
- EF/transaction interceptors وtest-only gate coordination في test composition مسموحة للfault/race observation إذا لا تغير production decision أو locks أو retry budget.
- لا catch-all fake نجاح، ولا test override لـ`ValidateLifetime`/signature validation/permission checks.
- Provider failures/cancellation لا تعاد كNotFound/Denied/Unavailable business outcome وفق T18؛ top-level classification يتبع T32.

---

# 11. Test Data وSecurity Material

- Synthetic data فقط؛ لا PII حقيقية أو Production dumps.
- Builders ذات defaults واضحة تعلن business preconditions؛ direct DB seeding مسموحة للـpreconditions التي ليست موضوع الاختبار.
- لا impossible seed state إلا عند اختبار defensive handling صراحة، ولا SQL seeding تتجاوز workflow المراد إثباتها.
- Unit GUIDs ثابتة مسموحة، و`Guid.NewGuid` مسموحة كsynthetic unique value خارج generator tests. UUID v7 generator تختبر version/shape/canonical form وفق T10.
- Randomness لsecurity material/uniqueness عند الحاجة، لا timing/race behavior أو random critical scenario inputs.
- RSA private keys وOTP HMAC keys وغيرها تولد runtime وتبقى memory أو scoped ephemeral storage عند الضرورة؛ لا Git/logs/reports/artifacts.
- Password/hash/OTP/proof/refresh/JWT/cookie raw values لا تظهر في asserts أو theory display names أو exception/log dumps. Raw OTP داخل sender spy memory فقط.
- Delivery tests تفحص destination/purpose/requested behavior ببيانات صناعية دون طباعة code.
- Crypto tests تثبت format/length/verify/rehash وsanity مناسبة، لا exact random/hash output أو statistical security guarantee وهمية.
- Temp filesystem paths معزولة، outputs ignored، وrun لا تعدل tracked files.

---

# 12. Deterministic Time وPrecision

Application time تتبع T11 عبر `IClock`، والـDomain تستقبل now صراحة. كل time-sensitive test تحدد reference instant وتستخدم `TestClock` بدل انتظار expiry/cooldown/lockout.

| Representation / boundary | Test increment |
|---|---|
| Pure .NET instant comparison | Tick عندما العقد فعليًا يحافظ عليها |
| PostgreSQL timestamp round-trip | Precision الخاصة بالعمود/provider؛ microsecond للtimestamp المعتادة |
| JWT NumericDate/encoded expiry | Encoded precision الفعلية؛ seconds في profile المعتمد، مع ClockSkew |
| DateOnly / calendar policy | Day/calendar/timezone boundary حسب T11 |
| TOTP | Time-step boundary وconfigured drift/replay window حسب T13 |

- نختبر before/exact/after وفق **الدقة التي تصل فعلًا إلى decision**. لا ننقل 100ns tick assertion إلى DB/JWT إذا representation تسقطها.
- UTC/DateTimeOffset وexplicit timezone؛ لا machine locale/timezone ضمنية. Africa/Cairo/calendar/DST cases عند صلتها.
- استبدال Application IClock لا يفترض التحكم تلقائيًا في JWT/framework/DB server clocks. Fixture توثق time source لكل component.
- Framework configurable clock تستخدم supported TimeProvider/clock seam بنفس Production validation semantics. مكون دون seam يدعم valid/expired scenarios بعيدًا عن boundary والClockSkew، وتختبر exact policy boundary في المستوى الذي يمكن التحكم في وقته دون bypass.
- No `Task.Delay`/`Thread.Sleep` لإثبات الوقت. Defensive timeout فقط يمنع hang ولا يحدد صحة السيناريو.

---

# 13. Controlled Concurrency وRace Fixtures

كل race test تعلن controlling row/constraint، competitor intents، synchronization point، allowed outcomes وfinal DB invariant قبل التنفيذ.

- كل competitor لها separate async scope/DbContext/connection؛ لا shared ChangeTracker ولا DbContext عبر concurrent tasks.
- الأفضل async `TaskCompletionSource` مع `RunContinuationsAsynchronously` أو bounded async gate. `Barrier` تستخدم فقط إذا لا تحبس execution resources أو تنتظر داخل critical section غير متاحة للجميع.
- Start gate قبل lock/claim المشتركة؛ لا barrier تتطلب وصول الجميع بعد الحصول على lock exclusive، لأن ده يخلق test-induced deadlock.
- لإثبات contention: A تصل إلى checkpoint بعد claim/lock وتنتظر release؛ B تبدأ وتثبت محاولة العملية/blocked state بobserver/interceptor أو PostgreSQL lock observation؛ ثم نسمح لـA بالCommit/Rollback.
- Read-only lock observation تستخدم test diagnostics وactual connection IDs؛ لا مجرد `Task.WhenAll`/start timestamps كدليل على overlap.
- Deadline دفاعية لكل gate/operation. Failure تنظف in-flight tasks وتُسجل، لا sleeps حتى ينجح السباق.
- لا نغير isolation/lock ordering/production timeouts لإخفاء defect. Timeout override محدودة لتجربة rejection branch توثق في fixture.
- لكل race حرجة scenario deterministic في normal suite؛ stability gate قد تعيدها 10 مرات fail-fast عند صلتها. أي failure تظل finding؛ النجاح اللاحق لا يمحوها.

---

# 14. Transactions وDeterministic Failure Injection

كل mutation ذات transaction تختبر commit-all وrollback-all وغياب success قبل Commit، مع authoritative DB assertions بfresh contexts بعد انتهاء المحاولة.

| Injection point | Required observation |
|---|---|
| Before mutation / before first save | No committed business effect |
| After one required write/save, before remaining required writes | Rollback of all transactional state |
| After required saves, before Commit | No partial Business/Outbox/Idempotency committed state |
| After actual Commit, before response delivery | Durable state exists؛ retry reconciles/replays one effect |
| Outcome cannot be queried authoritatively after ambiguous Commit | Classified OutcomeUnknown response، ثم same-intent reconciliation عند recovery |

طريقة الاختبار:

- Failure قبل Commit باستخدام scoped EF/transaction interceptor أو owning-flow checkpoint ضيقة داخل test composition؛ rollback/retry الإنتاجية تستمر كما هي.
- Lost response scenario تنفذ **Commit حقيقية** ثم test-side transport/response gate تمنع وصول response؛ تعيد نفس Key/OperationId عبر host قبل تكرار mutation.
- Unknown-outcome branch يمكن typed fault injection عند Commit acknowledgment/authoritative lookup boundary مع DB حقيقية؛ تفشل lookup بصورة صريحة، ثم ترفع fault وتثبت reconciliation.
- لا ترمي failure قبل Commit ثم تدعي أنها أثبتت successful-commit/response-lost scenario، ولا تغيّر fake persisted flag بدل فحص PostgreSQL.
- Provider exception propagation وboundary classification لها اختبارات مستقلة؛ لا catch-all Provider fake Business Error.
- Cancellation قرب Commit ليست proof of rollback. Tests تفصل client disconnect/server timeout/unclassified cancellation وunknown outcome حسب T15–T18/T32.
- Side-effecting Business/Audit/Outbox failure paths تطبق storage semantics الخاصة بها في T17؛ لا effect committed مع إسقاط required replay outcome.

---

# 15. API Standards Coverage — T31

| Contract | Verification |
|---|---|
| Routes/OpenAPI | `/api/v1`، lowercase kebab-case، unique method+route وOpenAPI operationId |
| Security metadata | One primary classification؛ explicit anonymous decisions؛ institution/idempotency metadata كاملة |
| Success semantics | 201 مع Location؛ 202 مع job/status reference؛ 204 بلا body |
| JSON/input | camelCase case-sensitive؛ reject unknown mutation fields/query params؛ reject duplicate scalar headers/query؛ unknown enum/code لا يصبح default |
| Representations | Canonical UUID/date/time/UTC/Money وnull/presence semantics؛ response DTOs لا Domain/EF entities |
| Queries | Page/Cursor envelopes، bounds، filter/sort allowlists وunknown fields |
| Caller OperationId | Valid non-empty canonical UUID input للعمليات المعنية؛ تمييزه عن OpenAPI operationId |
| Security/cache | Tenant/object authorization؛ sensitive classifications ترجع Cache-Control: no-store |
| Bulk | Maximum count، Atomic/Partial صريحة؛ clientItemId فريدة وitem results/counts متطابقة |
| Imports | Staging/validation/preview/confirmation/owner-module execution عند تنفيذ workflow |
| HTTP methods | GET لا business mutation؛ POST creation/commands؛ PUT/PATCH/DELETE تتبع domain semantics |

OpenAPI metadata tests لا تغني عن behavioral security tests. Route replacement تختبر new route وold route absence عندما no-alias جزء من القرار. Additive response changes والstable codes تراجع وفق T31.

---

# 16. Validation and Error Coverage — T32

هذه المصفوفة تنفذ قائمة T32 القسم 60.1 بالكامل:

| Behavior | Unit / registry checks | HTTP / integration checks |
|---|---|---|
| Error/Result invariants | Success/failure separation؛ primary Error واحدة؛ ValidationIssues/Truncated invariants | Invalid internal Result يخرج sanitized 500 |
| Public catalogs | Result code له Category واحدة؛ transport/exception fixed descriptors؛ safe per-code projection | Fixed type/title، actual status = body.status، stable code/detail safe |
| Unified writer | Shared schema/configuration checks | Result + binding + Challenge/Forbid + routing + rate limit + exception paths |
| Validation shape | Public path mapping، stable field code/description، dedup/order/cap | body/query/route/header locations؛ errors arrays من objects |
| Validation bounds | 50 issues وtruncation state | errorsTruncated الصحيحة؛ no empty errors/extensions |
| Correlation/trace | Safe generation/input normalization | X-Correlation-Id = body.correlationId؛ optional actual traceId؛ replay attempt diagnostics الحالية |
| Problem instance | Optional opaque occurrence URI | No request path/query/PII/resource identifiers |
| Protocol headers | Approved adapter rules | 401 WWW-Authenticate؛ 405 Allow؛ correct Retry-After |
| Transport errors | Fixed descriptors | 406/413/415/429؛ error media type؛ no HTML/fallback schema |
| Hidden resource | Same disclosure descriptor | Hidden/absent في نفس lookup لها نفس public status/type/title/code/detail/extensions |
| Metadata | Allowlist per public code، typed values/lengths، reject/suppress unsafe entries | No arbitrary object/internal constraint/version leak |
| Concurrency/availability | Known classifier inputs/phase/rollback/outcome | Busy/Timeout 503؛ unknown failure 500؛ no fake stale 409 |
| Cancellation/response lifecycle | Classified cancellation rules | Disconnect لا synthetic 500؛ started response لا ProblemDetails ثانية؛ HEAD بلا body |
| Partial Bulk | Stable item outcome mapping | clientItemId correlation؛ top-level vs item-level failure semantics |
| Sensitive disclosure | Safe Description by construction، no raw submitted values | No exception/SQL/stack/token/OTP/proof/PII leakage |

- Malformed JSON/unknown fields/scalar duplicates تختبر من actual input/binding path، لا handler validation وحدها.
- Security/transport failures قبل Handler لها adapter coverage حتى عندما `Accept` لا تسمح بصيغة success، وفق T32.
- Rollback/Commit/concurrency classification على PostgreSQL؛ registry-only tests ليست دليلًا على DB behavior.
- Edge errors التي ينتجها server/proxy قبل التطبيق توثق في Hosting coverage لاحقًا؛ لا تدعي TestServer أنها غطتها.

---

# 17. IdentityAccess and Authentication Matrix — T12/T13

| Area | Required scenarios عند تنفيذ الـFeature |
|---|---|
| Account/PersonIdentity | Approved lifecycle/linkage/uniqueness؛ no account-mode assumptions؛ suspension/closure effects |
| Passwords | Verify/rehash؛ unknown-account dummy verification؛ no raw secret persistence |
| Login | Phone/verified email؛ wrong/unverified/unknown account؛ anti-enumeration public failure؛ lockout boundaries؛ rehash؛ stale-password/concurrent login |
| JWT | alg/typ/kid/signature/issuer/audience/required claims/jti/exp؛ unknown kid؛ malformed/expired/revoked/session/security-version failures |
| Session | Effective expiration/idle/absolute limits؛ current server-side state؛ Version vs SecurityVersion separation |
| Refresh | A→B rotation/lineage؛ reuse revocation؛ same-A race؛ Logout/LogoutAll/reset races؛ rollback؛ cookie transport |
| OTP/proofs | Correct/wrong/max-attempt/cooldown/expiry؛ exact purpose/channel/target/account binding؛ single-use؛ no raw DB/log storage |
| Email | Candidate normalization/proof؛ atomic verified-email switch؛ normalized_email_address unique even before verification when non-null؛ concurrent duplicate |
| MFA/TOTP | Enrollment confirmation؛ challenge expiry/attempt limits؛ configured drift؛ same accepted time-step replay rejection؛ recovery-code single-use |
| StepUpGrant | Session/purpose/target binding؛ expiry/revocation؛ concurrent single-use consumption |
| OperationAuthorizationTicket | Session/module/operation/resource/OperationId binding؛ one ticket per grant؛ one target effect per ticket؛ same-intent retry after target rollback before expiry |
| Cookies/CORS/CSRF | HttpOnly/Secure/SameSite/Path/host-only/expiry؛ exact origins/credentials/methods/headers/exposed headers/preflight؛ missing/wrong Origin/CSRF deny before effect |
| Startup/environment | Missing/invalid JWT/OTP/session/origin/cookie/DB config fails closed؛ Development-only services absent in Production |

Critical races تتحقق من sessions/credentials/tickets/attempt counters/lineage/business effects النهائية، خصوصًا:

```text
Login vs Suspension / Password Reset
Concurrent failed logins preserve counters
Refresh vs same Refresh / Logout / LogoutAll / Security Reset
OTP / VerificationProof / RecoveryCode double consumption
Same TOTP time-step concurrent use succeeds once
Concurrent challenge failures preserve increments and max-attempt transition
One StepUpGrant issues one ticket
Concurrent ticket delivery creates one target business effect
Wrong ticket binding fails closed
```

Human descriptions لا تعتمد عليها assertions إلا إذا نص Product contract صريح. Public failure/body/timing-related behavior تتبع T12/T13؛ لا brittle elapsed-time benchmark لإثبات anti-enumeration.

---

# 18. Authorization and Typed InstitutionContext — T14

الشكل المشترك الذي تثبته Architecture/Unit tests:

```csharp
public interface IInstitutionContext
{
    Guid InstitutionId { get; }
    Guid UserAccountId { get; }
    InstitutionAccessKind AccessKind { get; }
}
```

لا `AccessRelationshipId` عامة في الـcore. Typed access contexts تحمل:

| Access path | Relationship facts |
|---|---|
| StaffMembership | InstitutionMembershipId |
| Student | StudentProfileId + InstitutionStudentRecordId |
| Guardian | GuardianRelationshipId + InstitutionStudentRecordId |
| PlatformSupport | PlatformCaseId + ExceptionalAccessGrantId |

نثبت scoped/immutable/server-validated/fail-closed context وعدم تبديل InstitutionId/AccessKind أو permission union/fallback خلال operation.

| Authorization behavior | Required proof |
|---|---|
| HTTP outcomes | 401 unauthenticated؛ 404 hidden/no-valid-relationship؛ 403 visible context with insufficient permission/assurance؛ authorized 2xx |
| Staff evaluator | Role/forbidden permission/capabilities/branch scope/assurance inputs matrix |
| Student/Guardian | Relationship/resource/status/scope/eligibility matrices مستقلة، ليست Staff roles |
| Mutable authorization | Membership/role/scope/capability/student/guardian/support state change يؤثر في next request دون JWT renewal |
| Branch/resource scope | Allowed/forbidden/institution-wide/missing scope؛ guessed IDs؛ route/body/header tampering |
| Access identity | Context relationship مرتبطة بالـActor الحالي؛ ID وحدها لا تكفي |
| Assurance | MFA enrollment/recent auth/exact Step-up purpose/target؛ no privilege bypass للـowner/admin |
| Wiring | Real resolver/evaluator/endpoint/handler integration، لا pure evaluator وحدها |
| Parallel isolation | Concurrent users/tenants/access kinds لا context bleed |
| Replay | Current permission/visibility rechecked؛ first-execution assurance/business rules تستثنى للCompleted |

Access eligibility تتبع 403/404، بينما Business outcome eligibility تتبع 422 حسب T32. No AccountMode/GeneralMode/StudentMode test assumptions.

---

# 19. Tenant Persistence and Constraints — T08/T09/T10

- Query filters: own tenant visible، foreign tenant hidden، missing context fail-closed؛ لا unscoped fallback.
- Writes: correct/wrong/missing trusted context وmulti-entity changes عند الصلة؛ forbidden mutation لا تترك partial state.
- Structural/tenant-aware composite FK وUnique/Check/partial indexes تختبر داخل وخارج predicates على PostgreSQL.
- Known constraint failures تصنف بالSQLSTATE + exact constraint identity؛ unknown failure لا تصبح invented business rejection.
- `IgnoreQueryFilters`/raw SQL paths لها exact allowlist وbehavioral coverage عند security sensitivity.
- IDs/timestamps/status/scope/schema ownership تثبت من persisted data، لا DTO وحدها.
- Assertion read تستخدم owning DbContext أو Npgsql بfresh connection لauthoritative post-condition؛ لا bypass execution path التي هي موضوع الاختبار.
- Required write guards/invariants تظل Production behavior، ولا تعطل أثناء test seeding إلا scenario defensive handling صريحة.

---

# 20. Concurrency and Academic Capacity — T16

| Scenario | Required final invariant / classification |
|---|---|
| Optimistic version | Protected mutation increments Version؛ stale decision rejects؛ semantic success/safe recompute/security race تصنف حسب use case |
| Version discipline | Aggregate-relevant child changes/ExecuteUpdate/raw SQL تلتزم T16؛ SecurityVersion مستقلة |
| Last seat | Different intents على capacity=1 → exactly one seat-consuming winner؛ occupied=1 |
| Capacity reduction | reserve vs lower-capacity تمثل legal serial order؛ occupied never exceeds capacity |
| Transfer | Opposite concurrent transfers؛ deterministic GroupId lock order؛ target/source state preserved؛ no partial transition |
| Academic constraint | Stricter proposal vs reservation؛ capacity vs accepted snapshot؛ initial inactive snapshot cannot consume seats |
| Constraint delivery | Same ConstraintChangeId/same intent dedup؛ different intent rejection؛ older versions/version gaps follow T16 |
| Limit activation | Higher limit not used before Academic activation؛ lower limit follows Enrollments acceptance protocol |
| Retry | 40P01/40001 whole-transaction retry only if approved/replay-safe/rollback confirmed؛ new context؛ same OperationId؛ bounded attempts |
| Busy/timeouts | 55P03/57014/25P04 classified by phase/rollback/cancellation؛ Busy/Timeout mapping follows T32 |
| Commit ambiguity | 40003/08007/connection loss follows T17 reconciliation؛ no blind write retry |

Seat controlling row ملك `Enrollments`، لا Academic StudyGroup. Final state دائمًا:

```text
OccupiedCapacity <= EnrollmentCapacity <= MaxAllowedEnrollmentCapacitySnapshot
```

لا SQL-text coupling أو query-count quota عامة. SQL clause/count assertion فقط لcontract حقيقية يصعب إثباتها سلوكيًا، مثل lock protocol أو منع N+1.

---

# 21. RecordCashPayment Named Exception — T15/T16/T17/T18

عند تنفيذ الـFeature يجب إثبات:

- Production orchestrator مملوكة لـStudentFinance؛ BranchFinance participant contract ضيقة؛ owning contexts enlisted في نفس open PostgreSQL connection/DbTransaction.
- One atomic outcome تشمل Payment + CashMovement + StudentFinance Idempotency Completed record + required participant-owned Outbox effects.
- Failure بعد Payment SaveChanges وقبل CashMovement، وبعد كلا save وقبل Commit، تعمل rollback للكل.
- RecordCashPayment vs CloseCashShift، وtwo payments on same shift، تلتزم `CashDrawer → CashShift` lock order والcurrent shift eligibility.
- Duplicate PaymentId/source constraint وsame-key/same-OperationId delivery تنشئ one logical effect.
- Approved deadlock retry تحفظ OperationId وتعيد whole boundary، وcommit/response-lost reconciliation لا تنشئ Payment ثانية.
- Success لا تظهر قبل shared Commit؛ tests تفحص schemas الاثنين وIdempotency/Outbox state بfresh contexts.
- Architecture تمنع Foreign DbContext injection/direct foreign writes/reverse dependency/generic transaction coordinator.
- الاستثناء لا يستخدم كpermission لshared transactions عامة.

---

# 22. Complete Idempotency Matrix — T17/T32

كل Required endpoint عند تنفيذها لها coverage تثبت:

| Scenario | Expected behavior |
|---|---|
| First execution + sequential replay | One committed effect؛ same safe semantic result/resource/job/OperationId |
| Invalid/missing/multiple key | 400 KeyRequired/KeyInvalid حسب T17/T31؛ no new claim/effect |
| Same key + different intent/OperationId | 409 Idempotency.KeyReuseMismatch |
| Same caller OperationId + different key/same intent | Semantic replay؛ no second effect |
| Same caller OperationId + different intent within trusted scope | 409 Idempotency.OperationIdReuseMismatch |
| Cross-user/tenant/operation scopes | No stored-result leak or accidental sharing |
| Global/no-institution scope | Correct NOT NULL trusted arbitration؛ exactly one winner |
| Concurrent winner commits | Loser reads committed Completed and resolves replay |
| Concurrent winner rolls back | Waiting competitor may claim and execute once |
| Loser reaches claim lock_timeout | Rollback loser؛ 409 RequestInProgress + Retry-After: 1؛ no business mutation |
| Processing lifecycle | No committed synchronous Processing-only reservation |
| Failure before confirmed Commit | No completed success/effect؛ failed-attempt rules follow T17 |
| Commit success + response lost | Same-key/OperationId retry authoritative-primary reconciles one effect |
| Outcome lookup unavailable | 503 OutcomeUnknown + Retry-After؛ client keeps same intent/key/OperationId |
| Current access revoked | Replay denied 403/404 without stored data |
| Completed after original ticket expiry/consumption | Replay allowed with current access؛ no fresh Step-up/ticket/business eligibility re-execution |
| No Completed record | Fresh current authorization/ticket required when policy says so |
| ReplayUntil/PurgeAfter | Before/exact/after boundaries؛ KeyExpired window؛ no unsupported guarantee after purge |
| Financial durable OperationId | Business uniqueness survives temporary idempotency record purge |
| Fingerprint | Golden vectors؛ canonical trusted scope/version； semantically equivalent requests؛ incompatible intent mismatch |
| Contract versions | Fingerprint/result adapters compatibility؛ unsupported saved result version fails deployment compatibility check |
| Replay payload | No raw request/secrets/cookies/transient URLs؛ size bound and persisted CHECKs؛ current diagnostics |
| Async job / offline attendance / webhook | Only feature-approved modes؛ job creation dedup؛ durable item/event identities حسب T17 عند تنفيذها |

- RecordCashPayment atomic receipt coverage في القسم 21.
- Metadata تعلن Required/SemanticallyIdempotent/SecuritySpecific/NotRequiredWithReason وفق T17/T31؛ لا inference من HTTP verb.
- Login/Refresh/OTP لا تستخدم generic secret replay؛ SecuritySpecific tests تتبع T13/T16.
- Unique arbitration وwinner/loser/rollback على PostgreSQL، لا process lock/fake dictionary كدليل.

---

# 23. Module Contracts and Architecture Enforcement — T18

| Rule | Test mechanism |
|---|---|
| Domain/Api/Application boundaries | Assembly/project/source checks عند ملاءمتها؛ no HTTP/EF dependency in Domain |
| Contracts public surface | Minimal immutable facts/results؛ no EF entities/IQueryable/DbContext/lazy enumerables/HTTP types |
| Dependency direction | Exact approved references؛ no cycles/reverse refs/foreign Infrastructure access |
| Provider semantics | Owns facts/outcomes؛ PG projections؛ missing vs infrastructure/cancellation distinguished |
| Consumer decisions | Uses facts for own rules؛ no automatic internal-error publication |
| Scoped access/actor | Typed neutral context shapes؛ no JWT role/capability source-of-truth shortcut |
| Cross-module persistence | No direct foreign DbContext/write/locks؛ named cash exception only |
| Cancellation/async/DI | Token propagation؛ await flow؛ lifetimes with actual isolation risk |
| Bulk contracts | Bounded multi-ID reads/counting spy when contract forbids N+1 |
| Account facts contract | IAccountFactsReader projection/consumer decision/no-PII/direction |

Reflection/project/source checks كافية كبداية؛ NetArchTest/ArchUnitNET ليست mandatory. Static search guard ليست بديلًا عن behavior، وتعرض violations واضحة دون secrets.

Allowlist محددة بالtype/path/use case وسبب القرار؛ لا wildcard يعطل rule كاملة. Architecture/public references لا تعطي Production access إضافية.

---

# 24. Test Quality and Coverage Policy

- One behavioral reason to fail؛ multiple assertions مسموحة لنفس contract/state transition.
- Valid/boundary/invalid paths للinvariants المهمة؛ negative/tamper/replay/expired scenarios للأمان عند الصلة.
- Stable code/category/facts/final state هي أساس assertions؛ full descriptions/private helper calls/internal ordering لا تثبت بلا contract.
- Bug fix قابل للأتمتة يضيف regression case؛ TDD ليست workflow إلزامية.
- No arbitrary line coverage أو numeric Unit/Integration ratio. المكان الذي يعتمد عليه correctness يحدد test level.
- Coverage reports ممكنة كsignal في T38؛ لا تعوض risk scenario ناقصة.
- Mutation/property-based testing ليست dependencies إلزامية؛ تقييمها لاحقًا على Domain/parsers المناسبة.
- Test counts دليل discovery/completeness، وليست هدفًا لزيادتها. Smoke-only test لا تعتبر Feature coverage.
- Source scans تقتصر على src/ tracked relevant files وتتجاهل bin/obj/generated outputs. Findings مثل test Guid.NewGuid تصنف واعيًا، لا auto-replace.
- No Internet/provider calls/real PII/committed secrets/silent skips/ordered tests/overspecified SQL أو timing correctness.

---

# 25. Flakiness, Skips and Failure Triage

- أي flaky test defect؛ ممنوع runner retry plugin أو retry-until-green.
- Rerun للتشخيص يسجل original failed attempt؛ stability repetitions fail-fast.
- Required security/acceptance tests لا تكون Skipped/Disabled/commented-out عند closure؛ no assert removal لتجاوز regression.
- Missing DB/configuration/Docker prerequisites تؤدي environment failure واضح للsuite المطلوبة.
- Interrupted/incomplete process ليست verification ناجحة؛ إعادة command مسموحة مع تسجيل interruption.
- Failure تصنف Production defect / Test defect / Environment defect / Flakiness / Approved requirement change، بدل افتراض أن test مخطئة.
- Verification-only gate توقف closure عند finding. Correction تتم في scope واضحة ثم rerun؛ لا production fixes صامتة داخل تقرير verification.
- في task تشمل implementation/fix، معالجة defect ضمن نفس authorization scope لا تحتاج handoff مصطنعة؛ المطلوب وضوح ما تغير وما أعيد التحقق منه.

---

# 26. Build, Run and Verification Cadence

الأوامر التالية baseline للSolution المخطط لها بعد إنشاء المشاريع وتجهيز test DB؛ لا تعتبر دليلًا على تشغيل فعلي بمجرد وجودها في T33.

```text
dotnet restore EduCenterOS.sln
dotnet build EduCenterOS.sln -c Release --no-restore
dotnet test EduCenterOS.sln -c Release --no-build
```

أثناء التنفيذ يمكن تشغيل suites منفردة:

```text
dotnet test tests/EduCenterOS.UnitTests/EduCenterOS.UnitTests.csproj -c Release --no-build
dotnet test tests/EduCenterOS.IntegrationTests/EduCenterOS.IntegrationTests.csproj -c Release --no-build
dotnet test tests/EduCenterOS.ArchitectureTests/EduCenterOS.ArchitectureTests.csproj -c Release --no-build
```

- Closure تشغل build ثم full required suite مرة واحدة. لا يلزم تشغيل suites الثلاث منفردة ثم full solution كتكرار افتراضي.
- Filtered tests أثناء التطوير/race diagnostic لا تغني عن full suite عندما closure تتطلبها.
- Release verification إلزامية للsecurity/Feature closure. Full Debug smoke عند صلتها بـconditional behavior أو final Release/Main verification؛ لا تكرار full runs بلا سبب.
- Warnings target: 0 errors و0 unjustified warnings؛ suppression مبررة فقط.
- Runner discovery gate تتحقق من non-zero executed tests لكل implemented required suite ومن ظهور critical scenarios. Green process مع zero-discovery لا تغلق العمل.
- Full solution prerequisites تشمل Integration DB؛ لا تسمى partial local Unit run full verification.
- بعد schema changes تنفذ fresh/pending-model/upgrade gates المطلوبة في القسم 8، منفصلة عن normal suite evidence.

---

# 27. Evidence, Reports and Git Scope

تقرير verification يذكر:

```text
Commands/configuration actually run + completion/exit status
Build errors/warnings
Per-suite Total / Passed / Failed / Skipped
PostgreSQL major + isolated target confirmation (redacted)
Migration contexts/order/fresh/upgrade/pending-model results when required
Critical race/fault scenarios + original failures/repetitions
Static scans and reviewed exceptions
Scope/diff/git state and verification limitations
```

- لا secrets/connection strings/raw tokens في evidence. Reports/artifacts output إلى ignored/temp paths، مع مراجعة redaction قبل مشاركتها.
- `git diff --check` hygiene عند وجود Git repository، وليس substitute للاختبار. Workspace بدون Git يذكر ذلك بدل اختلاق clean commit state.
- Verification لا تنتج empty/meaningless commits، ولا تغير Production files لمجرد test convenience.
- Tests changes تبقى في task scope؛ لا unrelated cases لزيادة العدد أو تغيير expectation لتناسب code خلاف القرار.
- Evidence مرتبطة بنتيجة مكتملة فعلًا؛ no report-only claim بأن DB/runner/security behavior ثبتت دون تشغيل المسار المناسب.

---

# 28. Definition of Ready / Done للFeature

Ready:

- Approved rule/public contract وrisk scenarios محددة.
- Test infra/prerequisites وtyped actor/context setup ممكنة دون bypass.
- PostgreSQL target معزولة عند persistence؛ time/race/fault seams مناسبة عند الحاجة.

Done:

- Relevant Unit/Integration/Architecture scenarios Green، مع negative/boundary coverage وفحص final DB state.
- T31/T32 checks عند API/Errors change، وT13–T18 matrices للflows المعنية.
- Required suites اكتشفت ونفذت tests فعلية؛ failed=0 وrequired critical skipped=0.
- Schema-sensitive work تحقق migrations/constraints/pending model والتغييرات على existing data عند صلتها.
- No unsafe cleanup/secrets/hidden dependencies/flaky timing؛ outputs disposed.
- Verification evidence صريحة ومتوافقة مع scope؛ behavior غير مختبرة تسجل كlimitation لا قبول ضمني.

Reviewer تفحص: requirement proved، setup لا bypass، DB/HTTP حقيقية حيث يلزم، overlap فعلي للرaces، final state، safe messages، expected negative paths وغياب false proof.

---

# 29. ما لا يحسمه T33

CI vendor، Docker Compose layout أو mandatory Testcontainers adoption، Observability backend، rate-limit values/penetration tooling، Hosting/edge test implementation، staging/deployment policy، Browser E2E framework، load platform، numeric coverage target، external sandbox cadence.

هذه تفاصيل tooling/operations في قراراتها. Contracts المطلوبة من real DB/runner/isolation/error/authorization لا تؤجل إليها.

---

# 30. Definition of Done لـT33

يعتبر القرار مقفولًا كمرجع للتنفيذ عندما تثبت فيه:

- xUnit generation وVSTest profile وpackages/version ownership.
- ثلاثة مستويات اختبار وحدود TestServer وdoubles واضحة.
- One test DB per worker/schema per Module وownership/cleanup guard/lifecycle.
- Migration order/history/fresh/pending-model وdata-upgrade expectations.
- Clock precision وcontrolled concurrency/failure-injection semantics.
- T31/T32 coverage كاملة، وtyped InstitutionContext متوافقة مع T14.
- T13–T18 risk matrices بما فيها cash/academic/MFA/ticket/idempotency scenarios.
- Conditional feature applicability دون تنفيذ المستقبل مبكرًا.
- Flaky/skip/discovery/cadence/evidence policies وملحق أمثلة التطبيق عند التنفيذ.

قبول T33 يعني اعتماد هذه العقود؛ تنفيذ Test Projects وتشغيلها يثبت في مهام البناء القادمة.

---

# ملحق A — أمثلة تطبيق سياسة الاختبار عند التنفيذ

هذه أمثلة لتغطية الاختبارات عندما تدخل الـFeature المعنية في نطاق التنفيذ المتفق عليه لاحقًا. لا تمثل خطة عمل معتمدة أو ترتيبًا زمنيًا، وتخضع لقواعد T33 العامة.

| Work area | Required coverage |
|---|---|
| Testing infrastructure | Test projects/runner discovery؛ isolated PG fixture/guard؛ safe Testing host؛ architecture checks |
| Email Verification | Candidate normalization؛ purpose/channel/target؛ wrong/expired/attempts/cooldown؛ proof single-use؛ atomic switch؛ uniqueness؛ SecurityVersion effect؛ login-after-verify؛ no secret leak |
| IAccountFactsReader | Provider Found/NotFound semantics؛ real PG minimal projection؛ consumer decision؛ cancellation/infrastructure propagation؛ no PII/secrets؛ dependency direction |
| Institution persistence | Module migrations/constraints؛ UUID v7/timestamps/status/scope/tenant integrity؛ one DB/schema coexistence |
| My Institutions | Current actor only؛ active relationship filtering؛ cross-user isolation؛ no JWT authorization-state source |
| Branch read | 401/hidden 404/visible-denied 403/success؛ tenant filter؛ guessed IDs؛ branch scope؛ context isolation |
| Authorization foundation | Pure evaluator matrix + HTTP resolver/evaluator wiring؛ mutable membership/capability/scope changes؛ typed context rules؛ cross-tenant read/write؛ no-context fail-closed |
| Security regression | Relevant Unit/PG Integration/Architecture؛ JWT/CSRF/CORS/startup negatives؛ parallel context isolation؛ static scans |
| Feature/Release verification | Release/full required suites؛ fresh all-context migrations/no pending model； API E2E؛ scoped evidence |

- Persistence task تحتاج dedicated PostgreSQL 18 environment؛ Docker/Testcontainers equivalent مقبولان.
- TestOtpSender/Test email sender من test composition؛ DevelopmentOtpSender ليست test infrastructure.
- Staff/Student/Guardian/PlatformSupport access تختبر حسب الـFeature الفعلية، دون fake Staff Membership لStudent/Guardian أو فرض علاقات مستقبلية على نطاق التنفيذ الحالي.
- MFA/financial/academic features غير المنفذة ليست skipped tests لإكمال عدد؛ تدخل required matrices عند إدخالها في الـscope.
- التنفيذ الجزئي للـFeature يعلن بوضوح؛ لا تدعي full Authorization/Tenant closure مع required negative scenario ناقصة.

---

# القرار النهائي المختصر

> EduCenterOS تعتمد xUnit.net v3 مع VSTest وثلاث Test Projects. Unit تختبر pure/domain logic، Integration تثبت HTTP/PostgreSQL behavior، وArchitecture تحمي حدود الموديولات. PostgreSQL 18 الحقيقية تستخدم Database معزولة لكل run/worker وبschemas مستقلة، مع ownership guard وreset/disposal lifecycle واضحة.

> الاختبارات تضبط الوقت بدقته الفعلية، وتجبر race overlap عبر async checkpoints خارج مواضع lock التي قد تحبس المنافسين. Failure injection تفصل rollback عن Commit ناجحة فقدت response، وتثبت authoritative reconciliation بدل blind retries.

> T31/T32 وT13–T18 لها matrices ملزمة عند تنفيذ الـFeatures المعنية، بما فيها typed context وMFA/tickets وacademic capacity وcash atomic exception وcomplete idempotency contract. Closure تتطلب tests منفذة وGreen دون required critical skips أو retry-until-green، وevidence واضحة؛ لا line-coverage percentage إلزامية.

---

## المراجع التنفيذية

- [xUnit.net v3 documentation](https://xunit.net/docs/getting-started/v3/getting-started): framework/runner profiles؛ أمثلة الوثائق التي تستخدم prerelease لا تغير Stable-only policy في T01.
- [xunit.v3 package](https://www.nuget.org/packages/xunit.v3): package family؛ الـversion المتوافقة تثبت مركزيًا عند scaffold.
- [dotnet test runner modes](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-dotnet-test): VSTest/MTP execution والاختلاف في arguments/configuration.
- [ASP.NET Core integration testing](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests): WebApplicationFactory/TestServer/test-host configuration وحدود المسار.
- [EF Core testing strategy](https://learn.microsoft.com/en-us/ef/core/testing/choosing-a-testing-strategy): real-provider behavior وحدود fake providers.
- [PostgreSQL 18 date/time types](https://www.postgresql.org/docs/18/datatype-datetime.html): stored precision المستخدمة في time-boundary tests.
