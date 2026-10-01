# T35 - Security Engineering

## الهدف من القرار

تثبيت سياسة أمنية موحدة وقابلة للتنفيذ والاختبار داخل EduCenterOS، تربط Authentication وAuthorization وTenant Isolation وAPI contracts وحماية البيانات دون إعادة تعريف القرارات المالكة لها.

المشروع مشروع تخرج وفي مرحلة تثبيت القرارات قبل التنفيذ. هذا الملف يحدد متطلبات الأمان عند تنفيذ الـFeatures، ولا يدعي وجود كود أو اختبارات ناجحة أو خطة تنفيذ معتمدة. Feature مستقبلية لا تنفذ مبكرًا لمجرد وجود ضوابطها هنا.

---

# القرار النهائي

```text
Secure by Design / Secure by Default
Least Privilege / Defense in Depth / Fail Closed
Explicit trust boundaries
Authoritative server-side authentication, authorization and tenant state
One validated access path with typed relationship contexts
Object-level + function-level + property-level authorization
Explicit DTOs + bounded input/output + anti-mass-assignment
T13 browser token / cookie / CSRF contract without weakening
T31/T32 safe HTTP contracts and Core V1 no-store baseline
Named risk-based rate-limit policies with validated central configuration
Data minimization / classification / masking / managed standard cryptography
No secrets in tracked files, logs, traces, URLs or generic replay
Provider-owned module contracts and explicitly approved atomic boundaries
Proportional threat review + real security tests under T33
Tailored ASVS 5.0.0 verification profile, not a compliance claim
```

---

# 1. المرجعية وحدود الاعتماد

- Business Rules المعتمدة، وبالأخص Business 14، تحدد المعنى والالتزامات التجارية والخصوصية.
- T01–T18 وT31/T32/T33 تحدد الـStack والحدود وعقود التنفيذ والاختبار.
- T35 تجمع هذه الضوابط وتضيف Security Engineering policy؛ لا تتجاوز قرارًا مالكًا لمجرد أن رقمها أحدث.
- أي تغيير في protocol أو ownership أو Business Rule يحتاج تعديلًا صريحًا في القرار المالك ومراجعة المراجع التابعة، لا override ضمنية.
- Requirement غير محسومة تسجل كقرار مطلوب قبل تفعيل الـFeature المعنية. لا نخترع Business restriction أو expected test behavior لتناسب التنفيذ.
- الاعتماد هنا اعتماد تصميم. إثبات التنفيذ يتطلب تشغيل المسار والاختبارات المناسبة فعلًا وفق T33.

---

# 2. Ownership وحدود القرارات التالية

| الموضوع | المرجع المالك |
|---|---|
| الهوية / PersonIdentity / uniqueness / OTP / verification proof | T12 |
| JWT / sessions / refresh / MFA / grants / operation tickets | T13 |
| Access paths / contexts / permissions / assurance requirements | T14 |
| Tenant isolation / schemas / integrity | T08/T09 مع شكل Context من T14 |
| Transactions / concurrency / idempotency / module contracts | T15/T16/T17/T18 |
| API / ProblemDetails / validation / verification | T31/T32/T33 |
| Security events / redaction rules | T35؛ sinks/metrics/alerts tooling في T34 |
| Configuration / secrets / key storage and loading | T36 |
| Local runtime / CI security automation / hosting and edge | T37/T38/T39 |
| Files / providers / jobs / reports / caches / search / audit | T23/T25/T19/T21/T28/T29/T30/T27 حسب الموضوع |

ذكر قرار لاحق لا يعني أنه موجود أو منفذ. الضابط الأمني ملزم عند انطباقه، بينما اختيار vendor/tool أو topology يبقى لقراره. لا تُفعّل Feature حساسة قبل اكتمال متطلباتها التنفيذية ذات الصلة.

---

# 3. External Verification Profile

نعتمد **OWASP ASVS 5.0.0** كمرجع verification ثابت الإصدار، و**OWASP API Security Top 10 2023** كقائمة مخاطر API، وليس كبديل عن acceptance criteria.

## 3.1 النطاق والمستوى

الـprofile المعتمدة للمشروع **Tailored risk-based subset**:

- تبدأ مراجعة الـFeature بالمتطلبات ذات الصلة من L1، وتضيف متطلبات L2 المناسبة للبيانات الشخصية والـTenant boundaries والـMFA والـLogs والـFiles.
- أي متطلب أعلى يفرضه البزنس، مثل approval أو تقليل كشف البيانات، يبقى ملزمًا بصرف النظر عن ASVS level.
- لا ندعي تحقيق L1 أو L2 كاملة أو Certification، ولا نفرض جميع فصول المعيار على مشروع التخرج.
- النطاق يشمل الـBackend والـfirst-party browser boundary والبيانات والـintegrations التي تُنفذ فعلًا. Frontend/edge controls تُسند لمالكها، ولا تعتبر مثبتة باختبار TestServer وحده.
- OAuth/OIDC وWebRTC وميزات غير معتمدة حاليًا لا تنشأ لتنفيذ فصول المعيار. يراجع انطباقها فقط إذا دخلت النطاق لاحقًا.
- قبل Production Release حقيقي يراجع نطاق ومستوى verification المطلوبان وفق البيانات والـdeployment الفعليين؛ جاهزية العرض الأكاديمي ليست تصريح تشغيل ببيانات حقيقية.

## 3.2 Initial Traceability Matrix

هذه المجموعة anchor requirements محددة للضوابط، وليست قائمة كاملة أو ادعاء اجتياز. IDs مثبتة بالإصدار؛ صيغة المرجع هي `v5.0.0-x.y.z`.

| ASVS reference | Project control | Owner / verification |
|---|---|---|
| `v5.0.0-1.2.4`, `v5.0.0-1.2.5` | Parameterized DB access؛ no command concatenation | Owning adapter؛ review + negative tests |
| `v5.0.0-1.3.6` | No arbitrary URL fetch؛ explicit SSRF controls | URL-fetch Feature قبل enabling |
| `v5.0.0-3.5.1`, `v5.0.0-3.5.2` | Cookie/Origin/non-simple request guards | T13؛ real HTTP/browser tests |
| `v5.0.0-5.2.1`, `v5.0.0-5.2.2`, `v5.0.0-5.2.3` | File/type/size/decompression bounds | T23/Import؛ upload/parser tests |
| `v5.0.0-6.1.1`, `v5.0.0-6.3.1` | Documented and enforced login abuse controls | IdentityAccess + API؛ limit/lockout tests |
| `v5.0.0-7.4.1`, `v5.0.0-7.4.2` | Server-side session termination | T12/T13؛ revocation tests |
| `v5.0.0-8.2.1`, `v5.0.0-8.2.2`, `v5.0.0-8.2.3` | Function/object/property authorization | T14؛ per-access-path matrix |
| `v5.0.0-8.4.1` | Cross-tenant controls | T09/T14؛ read/write/tamper/race tests |
| `v5.0.0-14.1.1`, `v5.0.0-14.1.2` | Classification and protection requirements | Business 14؛ data inventory/review |
| `v5.0.0-14.2.1`, `v5.0.0-14.3.2` | No secrets in URLs؛ no-store | T13/T31؛ absence/header tests |
| `v5.0.0-15.3.3`, `v5.0.0-15.3.4` | Mass-assignment prevention؛ trusted IP handling | DTOs + T39؛ tamper/config tests |
| `v5.0.0-16.2.5`, `v5.0.0-16.4.1` | Redaction and log-injection protection | T34/T35؛ sink-output tests |
| `v5.0.0-16.3.1`, `v5.0.0-16.3.2` | Authentication / denied-access events | Event owner؛ event contract tests |
| `v5.0.0-16.5.1`, `v5.0.0-16.5.3` | Sanitized errors and fail-closed behavior | T32؛ HTTP/fault tests |

عند تنفيذ Feature تحفظ مراجعة قصيرة: requirement ID، applicability، الضابط/مكانه، evidence، وحالة `Pending / Verified / NotApplicable / Exception` مع سبب وowner. عدم التنفيذ ليس `Verified` أو `NotApplicable`؛ Feature مستقبلية تبقى Pending إلى وقتها. أي Exception تتبع القسم 30 ولا تلغي Business Rule.

---

# 4. Security Principles

- **Secure by Design:** تدخل الضوابط في الـUse Case والـData Model والـContracts قبل التنفيذ.
- **Secure by Default:** endpoint ليست Public بالصدفة؛ missing tenant/secret/config لا تعني permissive fallback.
- **Least Privilege:** كل actor/service/module يملك أقل صلاحية وأقل بيانات لازمة.
- **Defense in Depth:** authn/authz/tenant/domain/DB/input/transport طبقات متكاملة.
- **Fail Closed:** عند تعذر إثبات شرط أمني مطلوب لا تحدث العملية. outage تظل infrastructure failure مصنفة وآمنة وفق T32، ولا تزور إلى NotFound/Inactive/Forbidden.
- IDs وUUID v7 وإخفاء routes وCORS وCSRF وVersion وIdempotency وTransactions وFeature flags ليست Authorization.
- Frontend button/route visibility وSwagger visibility ليست Security boundary.
- المتطلبات تنطبق server-side، ولا تتغير لتسهيل demo أو test.

---

# 5. Trust Boundaries وSecurity Signals

| المصدر | طريقة التعامل |
|---|---|
| Client body/query/route/header/cookie/file | Untrusted؛ bounded parsing ثم validation/security checks |
| JWT | Claims محدودة بعد strict validation؛ mutable state server-side |
| External provider/webhook | Untrusted input حتى بعد transport/signature checks |
| Foreign module facts | Provider-owned typed contract؛ source ownership محفوظة |
| Proxy/Host/IP/device | Trusted infrastructure config عند الصلة؛ signal لا identity |
| Jobs/events | Explicit execution scope؛ لا إعادة استخدام request actor ضمنيًا |

- المستخدم المسجل يمكنه تزوير IDs وRole وOwner flags؛ Authentication لا تجعل مدخلاته موثوقة.
- `X-Correlation-Id` للتتبع فقط، وليست actor/tenant/permission/idempotency authority. Normalization/bounds تتبع T31، ووجودها لا يسمح log injection.
- `OnboardingIntent` UX فقط؛ لا تمنح profile/relationship/role/permission.
- `UserAccount` هوية دخول وحالة أمنية، وليس Access Type أو Staff Role.
- IP/device signals قد تساعد anti-abuse، لكنها لا تثبت ملكية الحساب أو المؤسسة؛ لا hidden persistent fingerprinting خلاف T13.
- Secrets ليست Entity IDs؛ ولا تستخدم UUID/counter/System.Random لتوليد credentials.

---

# 6. Endpoint Security Contract

كل externally reachable application endpoint لها owner وعقد وإحدى classifications المعتمدة في T31:

```text
AnonymousSecurity
PublicRead
AuthenticatedUser
InstitutionScoped
PlatformScoped
```

- Public access صريحة في metadata/wiring والاختبارات؛ anonymous auth flow ليست Public data route.
- Protected endpoints تطبق authentication/authorization حتى لو لم تظهر في OpenAPI.
- GET/HEAD لا تنفذ business mutation؛ state changes عبر methods/actions المعتمدة في T31.
- Content-Type/Accept/status/headers وProblemDetails تتبع T31/T32، لا response shape خاصة بالأمان.
- Deprecated/debug/legacy endpoints لا تبقى attack surface بلا owner أو removal/version plan.
- Health/liveness responses العامة minimal؛ لا keys/connection strings/stack/topology حساسة. Diagnostics للمشغل المصرح له وفق T34/T39.

---

# 7. Institution Context وAccess Paths

T14 هي المرجع الوحيد لشكل الـContext. الـcore المشتركة:

```csharp
public interface IInstitutionContext
{
    Guid InstitutionId { get; }

    Guid UserAccountId { get; }

    InstitutionAccessKind AccessKind { get; }
}
```

**ممنوع إضافة `AccessRelationshipId` عامة للـcore.** Relationship references تكون typed وفق T14:

| Access path | Typed references |
|---|---|
| Staff | `InstitutionMembershipId` |
| Student | `StudentProfileId` + `InstitutionStudentRecordId` |
| Guardian | `GuardianRelationshipId` + `InstitutionStudentRecordId` |
| PlatformSupport | `PlatformCaseId` + `ExceptionalAccessGrantId` |

- Context Scoped/Immutable/Server-validated/Fail-closed؛ تنشأ مرة واحدة per operation scope.
- Route InstitutionId وrequested AccessKind مجرد candidates. السيرفر يتحقق من العلاقة الحالية وربطها بالـactor والـinstitution والـtarget عند الصلة.
- IDs وحدها لا تثبت ملكية العلاقة.
- Staff facts لا تُحمّل على Student/Guardian/PlatformSupport؛ كل علاقة تُقرأ من owner عبر T18.
- القرار يختار access path واحدة؛ لا fallback بعد Deny ولا merged Staff/Student/Guardian permission bag.
- لا تبديل InstitutionId/AccessKind أو mutable singleton/static tenant خلال العملية.
- Missing context لا تعني all tenants. Resource/business check إضافية لا تبدل context.

---

# 8. Authorization وVisibility

وجود علاقة صالحة يبدأ تقييم الوصول ولا يمنح كل صلاحيات المؤسسة. حسب الـUse Case نطبق:

```text
Current authentication and validated access path
+ function permission
+ object/resource access
+ property-level read/write rules
+ branch scope when relevant
+ access eligibility / assurance / approval / conflict requirements
```

- Permission catalog static/version-controlled؛ Roles/Scope/Capabilities/Relationships mutable facts من source of truth وفق T14.
- Mutable security/authorization changes تظهر في الطلب التالي دون انتظار JWT renewal. لا ad-hoc permission/session/tenant cache قبل T29/invalidation model معتمد.
- ملكية Object لا تسمح تلقائيًا بكل properties أو export/refund/admin function.
- Admin/PrimaryOwner لا يتجاوز conflict/approval rules.
- `401` للauthentication غير الصالحة، و`403` للمنع على سياق مسموح كشفه، و`404` عند hide policy وفق T14/T32.
- Hidden/absent لنفس lookup تستخدمان نفس public code/shape/metadata policy؛ لا leak لأسماء مؤسسات أو حالة علاقة مخفية.
- Branch/guardian/resource restriction لا تصبح `403` آليًا إذا policy تطلب إخفاء المورد.
- Business eligibility/state errors تظل `422` أو `409` حسب semantics المعتمدة؛ لا نصنف كل رفض كAuthorization.
- Login/recovery/verification failures تحافظ على anti-enumeration contracts؛ لا account-specific rate headers أو responses تكشف وجود حساب.
- لا ندعي constant-time HTTP؛ dummy password verification والمسارات المتقاربة وفق T13، وsecret comparisons تستخدم primitives المناسبة.

---

# 9. Tenant Isolation Layers

تطبق طبقات T09/T14/T08 معًا:

- Server-validated context ثم tenant-scoped query/projection.
- Query filters + explicit scope عندما يلزم + write guards.
- Tenant discriminators وDB integrity/constraints المعتمدة حيث تنطبق.
- Raw SQL/Read Models/Reports/Exports/Search/Storage/Cache لا تتجاوز هذه الحدود.
- Trusted context، وليس client InstitutionId، يحدد المؤسسة عند كتابة البيانات.
- Platform operation/cross-institution support تحتاج scope وصلاحية صريحة؛ لا generic tenant bypass أو null shortcut.
- الاختبارات تغطي guessed IDs وroute/body tampering وcross-tenant reads/writes وmissing context وparallel user/tenant isolation.
- Queries/guards/constraints لا تغني عن object/function/property authorization.

---

# 10. Identity وCredentials وSessions

T12/T13 مالكتان للتفاصيل؛ القواعد التالية تحفظ baseline ولا تغير lifetimes/protocols المعتمدة:

- Framework password hasher وrehash؛ لا plaintext/reversible password storage ولا support visibility/emailing.
- Password managers/paste/passphrases مسموحة؛ لا periodic rotation بلا security reason.
- Phone/email normalization وuniqueness DB-backed: `normalized_phone_number` فريدة، و`normalized_email_address` **UNIQUE whenever non-null، حتى قبل verification**. Verification تغير الثقة/Login eligibility لا uniqueness.
- PersonIdentity lifecycle لا تتغير هنا؛ لا إنشاء حساب لكل Student record تلقائيًا.
- OTP تستخدم CSPRNG وkeyed HMAC مع challenge/purpose/normalized-target binding وkey versioning؛ لا raw code persistence/logging.
- Expiry/attempts/resend/single-use/consumption تحمى atomically؛ الجديدة تبطل الأقدم لنفس Purpose+Target، ولا تعيد rolling abuse quotas بلا حدود.
- VerificationProof high-entropy/purpose-target-challenge-bound/short-lived/single-use؛ raw proof تظهر مرة واحدة، وتستهلك atomically مع العملية وفق T12.
- Refresh high-entropy opaque credential مخزنة hashed؛ strict rotation/reuse detection في T13. Generic retry/idempotency لا تعيد raw replacement refresh أو تخفي reuse.
- Access JWT `RS256` فقط مع strict alg/kid/typ/issuer/audience/claims/lifetime validation؛ no dynamic remote key URL.
- JWT لا تحمل phone/email/roles/permissions/institution/branch scopes. Account/session/SecurityVersion authoritative server-side.
- Successful login ينشئ Session جديدة server-side؛ client لا تختار sid.
- Suspension/Closure وsecurity reset/revocation تتبع T12/T13؛ لا انتظار JWT expiration لقبول سحب الوصول.
- SecurityVersion منفصلة عن concurrency Version وpermission state.
- Recovery وتغيير phone/email/MFA sensitive workflows؛ لا UX shortcut أو manual sensitive recovery خارج approved process.
- Temporary lockout ليست suspension. Baseline T13: 5 failed attempts ثم 5-minute lockout؛ القيم مركزية، مع atomic counters وعدم malicious permanent lockout.

---

# 11. MFA / Step-up / Idempotent Replay

- IdentityAccess وحدها تملك MFA secrets/methods/state/recent-auth evidence/grants/tickets.
- T14/Business owner تحدد required assurance/approval؛ Core V1 تنفذ TOTP وhashed single-use Recovery Codes للسياقات الحساسة حسب T13.
- SMS OTP ليست permanent second factor للأدوار الحساسة.
- Step-up لا تضيف permission/relationship/branch scope؛ Session MFA flag ليست تصريحًا مفتوحًا لكل عملية.
- High-risk flow تستهلك StepUpGrant داخل IdentityAccess وتصدر OperationAuthorizationTicket مقيدة بالsession/module/operation/resource/OperationId وexpiry وفق T13.
- Target Module تتحقق من current access وticket binding لأول تنفيذ، وتحفظ TicketId+OperationId مع mutation وuniqueness داخل owning transaction.
- لا cross-module transaction بين IdentityAccess والـBusiness Module لإصدار/استخدام التذكرة.

| المسار | التحقق المطلوب |
|---|---|
| First execution / no Completed record | Current authentication/context/access + current business rules + fresh valid assurance/ticket عندما تتطلب policy |
| Completed replay | Current authentication/context/permission/resource visibility + same trusted scope/key/fingerprint/OperationId وفق T17 |
| Permission/visibility revoked | Deny وفق 403/404؛ لا كشف stored result |
| Credential rotation/OTP/proof flow | Security-specific replay semantics؛ لا generic secret replay |

Completed replay لا تعيد first-execution eligibility/state-transition rules، ولا تطلب fresh step-up أو بقاء/إعادة استهلاك ticket الأصلية. Missing Completed lookup لا تتجاوز assurance لأول تنفيذ. Raw ticket/grant/proof/refresh/OTP secrets لا تحفظ في fingerprint أو generic replay payload.

---

# 12. Browser Token / Cookie / CSRF Contract

Core V1 first-party browser فقط وفق T13؛ Native client تحتاج قرار transport/storage مستقل.

- Access token ترجع JSON وتحفظ **Memory only**؛ لا localStorage/sessionStorage/IndexedDB.
- Refresh لا ترجع JSON؛ cookie `__Secure-educenteros-refresh`، `Secure`، `HttpOnly`، `SameSite=Strict`، host-only بدون Domain، وexact shared auth Path اللازمة للrefresh/logout.
- Cookie expiry لا تتجاوز credential expiry. Delete/expire تستخدم نفس name/path/domain policy.
- Business APIs تستخدم Authorization: Bearer؛ refresh cookie ليست general Cookie Authentication.
- Path تضييق إرسال فقط وليست Security boundary؛ لا استبدالها بAuthorization/CSRF.
- Browser/auth deployment same-site compatible مع Strict؛ cross-site يحتاج قرار جديد، لا تخفيف صامت.
- Refresh/logout browser flows: POST only + exact allowed Origin + strict CORS + required non-simple custom header + rejection of simple content types + rejection of missing/mismatched Origin.
- Core V1 تختار `X-EduCenterOS-CSRF: 1` كاسم/value ثابتين للـnon-simple request guard، allow-listed في CORS؛ ليست secret أو synchronizer token ولا Authentication proof.
- Origin matching تكون scheme+host+port exact normalized match؛ لا contains/suffix wildcard ولا `null` Origin.
- SameSite/CORS طبقات إضافية؛ السيرفر يطبق Origin/header/content-type guards فعليًا.
- CORS exact configured origins؛ no wildcard/reflection permissive with credentials. Methods/headers explicit، وAllowCredentials فقط للflows التي تحتاجها.
- Allowlist تشمل Authorization/Content-Type/custom CSRF/Idempotency-Key وغيرها فقط حسب endpoints الفعلية؛ expose headers حسب T31/القسم 18.
- Origin guard ليست شرطًا عامًا يفرض على كل bearer request غير browser؛ threat model لكل surface محفوظة.
- Real HTTP tests تثبت server rejection/headers؛ browser tests عند الحاجة تثبت إرسال وحذف cookie/preflight وفق T33، ولا يفترض TestServer محاكاة browser rules.

## 12.1 Cache-Control Baseline

كل تصنيفات T31 في Core V1 تستخدم `Cache-Control: no-store`، بما فيها AnonymousSecurity/AuthenticatedUser/InstitutionScoped/PlatformScoped، وPublicRead افتراضيًا إلى اعتماد cache policy صريحة في T29.

ينطبق ذلك على credentials/tickets/PII/financial responses وProblems الحساسة أيضًا. File/presigned responses تتبع T23 دون تخفيف حماية بياناتها. لا shared/public sensitive cache أو ad-hoc security cache داخل Feature.

---

# 13. Transport / Host / Proxy Controls

- Production external traffic تستخدم TLS؛ لا disabled certificate validation مع platform/provider/storage endpoints.
- Sensitive API plaintext لا تخدم business work. T39 تحدد rejection/edge termination؛ transparent redirect لا يعالج credential سبق إرسالها عبر HTTP.
- HSTS عند browser HTTPS surface؛ T39 تحدد تطبيقها في edge أو app بدون تضارب أو preload/includeSubDomains بلا مراجعة topology.
- Forwarded headers تقبل فقط من known proxies/networks مع hop policy معتمدة. Internet X-Forwarded-For ليست source IP موثوقة.
- Host allowlist/config validated؛ verification/invitation/reset URLs تبنى من trusted configured base URL، لا raw Host/request forwarding.
- CSP/Referrer-Policy/X-Content-Type-Options وغيرها تطبق وفق surface؛ CSP للHTML/JS لا كإثبات أمان JSON API.
- Headers/Location/filenames لا تدمج user input raw؛ تمنع CRLF/header injection.
- returnUrl/redirect targets local أو exact allow-listed؛ لا open redirects.
- TLS/proxy/HSTS/browser evidence لا تعتبر مثبتة بمجرد passing in-process HTTP tests؛ deployment verification في T39.

---

# 14. Input / Injection / DTO Controls

الـAPI layer تملك transport/request-shape validation، والـApplication تملك request rules/security orchestration، والـDomain تملك invariants. لا ClaimsPrincipal/HttpContext/permission infrastructure داخل Domain.

- Request/response DTOs صريحة؛ no entity binding/dump. Status/OwnerId/Role/SecurityVersion/ApprovalState/InstitutionId لا تعدل بمجرد إرسالها في body.
- camelCase case-sensitive؛ reject unknown mutation fields وunknown query parameters وduplicate scalar query/header values حسب T31/T32.
- Invalid enum/code لا يصبح default value. Unsafe polymorphic type selection/serializer type metadata من client ممنوعة.
- Validation errors لا تعيد raw input/serializer exceptions/CLR types؛ bounded deterministic field-error contract من T32.
- Malformed shape ليست permission failure. لكن لا نفترض أن كل validation يجب أن تسبق Authentication؛ actual pipeline/order يتبع T31/T32 ويحافظ على hidden-resource semantics.
- Shape validation لبناء fingerprint لا تعني تحميل business state أو fresh assurance قبل Completed replay lookup.
- SQL تستخدم parameters/EF؛ dynamic identifiers/sort/filter/operators تستخدم explicit allowlist mapping، لا client expressions/reflection/SQL fragments.
- Shell/process arguments لا تبنى بconcatenation لuntrusted data؛ لا arbitrary command execution.
- Paths/storage keys داخليًا generated/validated؛ لا تحويل filename/request path إلى filesystem path مباشرة.
- HTML/Markdown المستقبلية تحتاج sanitization/output-encoding وsafe rendering contract؛ JSON أو authenticated source لا يجعل markup trusted.
- Regex bounded input + timeout/non-backtracking عند الصلة؛ لا client-provided regex في Core V1.

---

# 15. Resource Bounds وExpensive Work

كل endpoint تحدد bounds قابلة للاختبار للbody/string/list/page/filter/response/work. Defaults لا تعني قبول sizes غير محدودة حتى يختار المطور أرقامًا.

- Core V1 normal JSON request body baseline = **1 MiB**. Streaming/chunked input تُقاس فعليًا؛ Content-Length وحدها لا تكفي.
- Smaller per-endpoint limits مطلوبة للauth/OTP متى كانت مناسبة. File/multipart/import endpoints تحتاج عقد bounds مستقلة في T23/Business 15، لا ترث exemption غير محدودة.
- String lengths وcollection maxima تحددها Request contract؛ pagination/cursor limits وvalidation semantics تتبع T31.
- `413` لbody تجاوز transport limit، و`400` لrequest field/count خارج العقد حسب T32؛ لا invented status.
- Export/report/import/search تُقيّم CPU/memory/DB/response/storage/provider cost، مع bounded scope وpagination أو approved asynchronous workflow عند الحاجة.
- لا arbitrary query language أو unlimited rows. Bulk partial-result protocol تتبع T31/T32، ولا تختلط unauthorized items بPII disclosure.
- DB/external/network work لها timeouts/cancellation؛ transaction timeout/retry policy تتبع T15/T16، لا timeout tuning داخل Feature يخالفها.
- RequestAborted تمر للI/O؛ cancellation ليست success ولا proof of rollback خصوصًا قرب Commit.
- No unbounded fire-and-forget work في request path؛ durable jobs/events تتبع T19/T21.
- Concurrency limiter للعمليات المكلفة لا يعوض time-based abuse limits. Application controls لا تعد بمنع volumetric DDoS؛ edge في T39.

---

# 16. Named Rate-limit Policies وInitial Baselines

نعتمد ASP.NET Core rate-limiting primitives للAPI budgets، مع IdentityAccess atomic/persistent counters والOTP/lockout state حيث تتطلب T12/T13/T16. لا نكتب crypto أو distributed limiter framework خاصة.

القيم التالية **initial configurable technical baselines** للمشروع، وليست Business constants أو ضمان أنها مناسبة لكل Production load. تضبط في typed options وتراجع باختبارات السياسة وbounded load قبل الإطلاق الحقيقي.

كل limit في الصف تطبق مع الحدود الأخرى، لا كبدائل. Time-based profiles تستخدم sliding windows؛ baseline `SegmentsPerWindow = 4` حيث ينطبق، و`QueueLimit = 0` للرفض الفوري دون تجميع credentials أو provider calls داخل queue.

| Policy | Applies to | Partition / initial request budget |
|---|---|---|
| `AnonymousIngress` | AnonymousSecurity/PublicRead application routes | Trusted source IP signal: 120/minute؛ defense إضافية قبل expensive work |
| `LoginAttempt` | Password login | Source IP: 30/minute + normalized identifier: 10/15 minutes؛ existing T13 account lockout مستقل |
| `OtpIssue` | Initial issue + resend | Source IP: 20/15 minutes + normalized target: 3/15 minutes across purposes + minimum resend interval 60 seconds |
| `OtpVerify` | OTP verification | Source IP: 30/minute + normalized target: 10/15 minutes across challenges/purposes؛ challenge attempt/expiry policy من T12 |
| `AccountRecovery` | Recovery initiation | Source IP: 10/15 minutes + normalized target: 3/30 minutes؛ delivery كذلك تخضع OtpIssue/provider quota |
| `MfaAttempt` | MFA login challenge / step-up verification / recovery-code attempts | Source IP: 30/minute + server-resolved account: 10/5 minutes؛ challenge attempts/single-use controls مستقلة |
| `RefreshAttempt` | Browser refresh | Source IP: 60/minute + server-resolved active session: 30/minute؛ malformed/unknown credential لا تتجاوز IP budget |
| `SensitiveDelivery` | Enabled invitation/notification/recovery delivery actions | authenticated actor: 10/15 minutes؛ destination/provider budgets required before enabling |
| `ExpensiveRead` | Enabled high-cost search/report routes | authenticated actor: 60/minute + trusted institution: 300/minute عندما tenant-scoped |
| `BulkExport` | Enabled export initiation | actor: 5/10 minutes + concurrency 1/actor و2/trusted institution |

- أسماء policies conventions مركزية، لا authorization codes أو Business operation IDs. Endpoint inventory تسجل policies المطلوبة؛ architecture/contract tests تمنع missing registration/wiring.
- لا policy تجعل unsupported Feature منفذة؛ invitations/exports/providers تبقى مرتبطة بقراراتها وReady requirements.
- Identifier/target partitions تحسب للمدخلات normalized حتى لو الحساب غير موجود؛ لا split public response based on resolved existence.
- Unknown/malformed identifier يستخدم fallback bounded budget بعد input bounds، لا unlimited/no-limit path. Raw password/token/OTP ليست partition key.
- OTP resend لا تعيد budget عبر إنشاء Challenge أو تغيير purpose، وprovider failure لا يسمح بغير محدود من الإرسال.
- Minimum resend interval ليست lifetime أو إعادة تعريف proof/OTP protocol؛ تدار مركزيًا مع باقي T12 policy.
- Logout/revocation تحمى من abuse بسياسة موثقة لا تمنع المستخدم من إبطال جلسة لمجرد انتهاء Access JWT؛ T13 no-op/delete behavior محفوظة. لا نجمع refresh/logout تحت session budget يصنع logout denial دائمًا.
- Provider quotas/concurrency وتكلفة SMS/email تضبط حسب provider configuration قبل enabling. لا قيمة global مخترعة تدعي تغطية كل vendor.
- لا CAPTCHA افتراضية؛ إن أضيفت فهي defense إضافية وليست بديلًا عن authorization/limits.

---

# 17. Rate-limit Trust / Bounded State / Runtime Scope

## 17.1 Partition authority

- UserAccountId من validated authentication، session من server-side resolution، InstitutionId من trusted context. Client headers/body IDs لا تمنح authenticated/tenant quota جديدة.
- Anonymous IP من trusted forwarding configuration/RemoteIpAddress، لا raw X-Forwarded-For. Missing IP تستخدم bounded shared fallback لا Fail Open.
- Device/User-Agent/X-Device-Id signals إضافية فقط؛ ليست sole limiter key قابلة للتبديل.
- Identifier/target تستخدم normalized keyed digest عند matching/retention؛ لا raw PII في telemetry أو metric labels. Hashing وحدها لا تحد عدد partitions.
- Anonymous ingress/IP/identifier defenses تطبق قبل password hashing/provider calls أو decoding غير bounded. Account/session/tenant-specific limits تأتي بعد trusted resolution المناسب؛ هذا ترتيب أمني مفاهيمي، لا middleware-order يفترض كل facts قبل Authentication.

## 17.2 Prevent unbounded partition creation

Core V1 تستخدم fixed buckets للـAPI throttle signals بدل registry غير محدودة لكل arbitrary submitted value أو active actor/session. Actual identity/security state تظل في owning persistence، لا buckets:

```text
bounded validated signal
→ keyed digest
→ bucket index in [0, RateLimitBucketCount)
```

- `RateLimitBucketCount = 4096` baseline مركزية لكل configured policy/signal dimension؛ no client-selected policy names أو arbitrary route/URL dimensions. العدد الكلي محدود بعدد policies/dimensions المسجلة، وليس عدد requests/users.
- Authenticated signals تدخل بعد validated resolution ثم تستخدم نفس bounded-bucket approach؛ لا registry مخصصة أو allocation لكل UserAccountId/SessionId جديدة.
- Budget الصف في القسم 16 حد أعلى للsignal؛ مشاركة bucket قد تؤدي conservative throttling/concurrency sharing. Collisions لا تسمح تجاوز limit ولا تغيير identity/account lockout state لحساب آخر.
- هذه abuse buckets ليست account identity أو authorization scope؛ لا raw digest/bucket attribution في public ProblemDetails.
- Implementation تستخدم framework partitioned limiter بقيمة bucket bounded؛ لا نكتب limiter algorithm أو custom per-actor registry. Supported helpers/cleanup تستخدم بدل timer مستقلة لكل request.
- Memory/bucket collision/concurrency/load behavior تختبر بالقيم المطبقة؛ تغيير عدد buckets لا يفتح unbounded cardinality. Exact per-account/target budgets المطلوبة أمنيًا تنفذ أيضًا atomically في IdentityAccess حسب owning policy، لا تستبدل ببucket approximation.
- لا high-cardinality user/IP/target values في metrics؛ policy/outcome aggregates فقط.

## 17.3 Enforcement scope

- In-process limiter budget **per instance**؛ process restart أو replicas لا تحفظ cluster-wide budget.
- Local/demo single-instance use مقبول للAPI throttling مع توثيق هذا القيد. ليس إثبات coordinated Production protection.
- Durable security state مثل account failed attempts/lockout وchallenge attempts/single-use وresend invalidation تبقى في owning IdentityAccess persistence، لا limiter memory.
- Rolling cross-challenge delivery quotas المطلوبة في T12 لا تختفي لمجرد resend أو handler restart؛ تختار لها IdentityAccess atomic persistent enforcement عند التنفيذ، بدون raw destination retention زائد.
- Hosting متعدد instances يحتاج coordinated policy أو aggregate-budget design معتمد قبل activation؛ vendor/storage mechanism في T36/T39، ولا نفرض Redis/WAF لمجرد مشروع التخرج.
- Actual quota-store outage لا تتحول إلى fake 429 أو account inactive؛ sensitive delivery توقف قبل provider side effect وتعود classified sanitized failure حسب T32.
- Technical rate limit لا يساوي lockout أو ban. Anomaly لا تمنح auto-suspension بلا approved Business policy.

---

# 18. Rate-limit HTTP Contract وConfiguration

الرفض داخل التطبيق يستخدم writer T32 نفسها، بما فيه الرفض قبل business Handler:

| Field | Contract |
|---|---|
| HTTP status | `429 Too Many Requests` |
| Content-Type | `application/problem+json` |
| ErrorCategory | existing `RateLimited` من T32 |
| public code | `Infrastructure.RateLimitExceeded`؛ owner = CommonInfrastructure/API security adapter |
| type | `urn:educenteros:problem:rate-limited` |
| title | `Rate limit exceeded` |
| detail | fixed safe client text، لا target/account/remaining-attempt values |
| correlation | current X-Correlation-Id + matching correlationId وفق T31/T32 |
| traceId | only when Activity exists؛ لا body-field جديدة مفروضة |
| metadata/errors | none by default؛ no bucket/identifier/account-state disclosure |
| Cache-Control | `no-store` |

- Code تسجل في T32 registry implementation بCategory/owner/metadata allowlist فارغة، لا public unregistered code أو تعديل ErrorCategory.
- `Retry-After` فقط عندما limiter لديها known valid retry delay: positive whole seconds بتقريب لأعلى وحد أدنى 1. لا وقت مشتق من existence/lockout state أو guessed duration.
- Concurrency rejection لا تختلق وقت اكتمال الطلب الجاري. Dynamic delay غير معروف: لا Retry-After مفبركة.
- Core V1 لا ترسل additional `RateLimit-*`/`X-RateLimit-*` limit/remaining/reset headers؛ إضافتها تحتاج explicit safe contract، لا default package leakage.
- CORS expose `Retry-After` عند الحاجة للbrowser مع response headers المعتمدة في T31، ولا تفتح كل headers تلقائيًا.
- Edge-generated rejection contract في T39؛ لا ادعاء أن writer التطبيق تتحكم في edge.
- Typed central options تشمل policy/windows/permits/segments/queues/bucket count/required destination/provider budgets. ValidateOnStart/feature activation validation: positive bounded values، finite policy/dimension inventory، queue disabled للsecurity flows، policies present، no permissive missing fallback.
- T13/T12 تبقيان owners للcredential lifetimes/lockout/protocol parameters. تخفيف controls أو تغيير key derivation/window semantics يحتاج security review؛ normal options لا تحمل IOptions/IConfiguration إلى Domain.

---

# 19. Data Classification / Minimization / Privacy

Business 14 هي المرجع: Public/Internal/Personal/Highly Sensitive/Security Secrets. لكل field/data store purpose وprotection/retention/access policy مناسبة، لا جمع البيانات «احتياطًا».

- DTO projection بأقل fields؛ absence tests للpassword/hash/OTP/proof/refresh/JWT internals والPII غير اللازمة.
- Masking server-side عندما full value غير لازمة؛ لا إرسال القيمة كاملة ثم إخفاؤها في UI.
- Full reveal للبيانات الحساسة يحتاج permission/resource access وstep-up/audit/approval حيث يطلب البزنس. Platform/support لا تملك browsing عام لبيانات المؤسسات.
- National ID ليست login/password/recovery secret. تخزينها المشروع يتطلب encrypted value + secure matching representation وفق Business 14/T12 مع managed keys؛ plain hash لقيم يمكن تخمينها ليست سرية كافية.
- Identity docs/bank/payout values/backups/secrets لها at-rest protection حسب data classification/threat model. Encryption library/store في T23/T25/T36/T39 لا تعني السماح بتخزين raw values حتى ذلك الوقت.
- لا PAN/CVV/PIN/wallet password/provider payment OTP persistence خلاف Business 14.
- No credentials/private proofs/full identity values في URLs/query/analytics/tracking/referrer/logs/tickets.
- Consent/minors/guardian privacy requests والمعالجة عبر المؤسسات تتبع Business 14؛ لا technical permission توسع purpose أو guardian relationship.
- Third-party payloads/notifications تحوي minimum necessary fields؛ لا بيانات عالية الحساسية لـadvertising providers أو trackers.
- Retention/deletion/anonymization مركزية وفق Business 14؛ account deletion لا تمحو audit/legal/security evidence المُلزمة، ولا تحتفظ بكل البيانات بلا مدة.
- Backups بنفس sensitivity/access/encryption/retention؛ لا archive أبدي أو privacy escape.
- No raw Production DB dumps في Dev/Test؛ synthetic أو sanitized/anonymized dataset وفق policy.
- Offline attendance future controls تتبع Business 14/05؛ لا تخزين credentials أو financial/admin operations offline في Core V1، ولا استنتاج Native token model من offline cache.

---

# 20. Secrets / Cryptography / Rotation

| Purpose | Approved approach |
|---|---|
| Human password | framework adaptive password hashing وفق T12 |
| High-entropy refresh/proof/recovery secret | one-way hash/HMAC وفق owning protocol |
| Low-entropy OTP / sensitive matching | keyed HMAC مع purpose/binding/key version مناسب |
| Recoverable sensitive plaintext / TOTP secret | managed authenticated encryption / framework primitives حسب owner |
| Authenticity / JWT / webhook | standard signature/HMAC والتحقق حسب protocol |

- لا custom algorithms ولا System.Random/predictable GUID secrets. Constant-time comparisons حيث protocol تتطلبها.
- Encryption design تشمل key versioning/rotation/access control وintegrity/authentication؛ لا nonce/IV reuse أو single hardcoded key.
- Keys/connection strings/provider credentials/OTP HMAC/recovery secrets لا تدخل tracked appsettings/Git/logs/trace/error output.
- Production missing key/secret/config تفشل startup أو sensitive feature activation، لا dev fallback أو silently generated signing key.
- Previous JWT validation keys public material only وفق T13؛ no private key in validation slot ولا remote client-provided key source.
- Runtime secrets copies/lifetimes تقلل قدر الإمكان؛ لا static global secret strings غير لازمة، ولا ادعاء guaranteed managed-memory erasure.
- T36 تحسم loading/store/Data Protection key ring/at-rest key persistence/rotation. Feature تحتاج recoverable secret لا تفعّل قبل إدارة مفاتيحها الفعلية.

---

# 21. File Uploads / Imports / Downloads

تطبق عند تنفيذ T23/Business 15 والFeatures المعنية، لا تنشئ upload endpoint عامة مسبقًا:

- Explicit type allowlist + actual content verification؛ extension/Content-Type/magic bytes وحدها ليست ضمان parser safety.
- Size/count/storage quotas وbounded processing؛ generated storage names، private non-executable storage، authorization قبل وصول/تغيير object.
- Malware scanning حسب Business 14، وquarantine قبل availability حيث تتطلب policy. Scanner/vendor يختار لاحقًا؛ no-op scan لا تسمى protection، والخدمة غير المتاحة لا تعتبر file safe تلقائيًا.
- HTML/SVG/executable/macro-enabled content لا يعرض inline كtrusted active content؛ sanitize أو reject أو isolated serving policy قبل enabling.
- Archive/OOXML imports تحد compressed/uncompressed bytes وentry count/path/symlink handling قبل/أثناء decompression؛ no zip slip/bombs أو macro execution.
- Image dimensions/decoded memory bounded؛ large pixel count ليست آمنة لمجرد صغر compressed size.
- Excel parser bounded rows/columns/cell lengths ونوع الملف؛ no external-link/formula/macro execution أو tenant bypass. Spreadsheet exports تحتاج formula-injection-safe output عند client rendering وفق feature contract.
- Sensitive metadata/EXIF ونحوها تقلل أو تنقح حسب use case.
- Private download/signed URL لا تصدر إلا بعد current authorization؛ short-lived ومقيدة resource/method حسب storage design. Prefix/key/guessable path ليست access check.
- Generated export filename لا يكشف sensitive values؛ retention/cleanup/download audit تتبع owner.
- No upload/download activation قبل تحديد storage/access/encryption/scan/serving/retention contract واختبارها.

---

# 22. URL Fetch / External Providers / Webhooks

- Generic arbitrary URL-fetch endpoint ممنوعة. Feature معتمدة تحتاج scheme/host/port allowlist، private/loopback/link-local/metadata-network denial، validated DNS/connect destination، redirect revalidation أو disable، timeout/size/connection bounds وTLS validation.
- Application checks تتكامل مع egress controls عند hosting؛ لا string host check تدعي منع DNS rebinding أو redirects وحدها.
- Provider response schema/bounds/status/identifier/amount/currency validation عند الصلة؛ signature لا تجعل business payload truth غير محتاجة تحقق.
- Webhooks: provider-specified signature/auth protocol على exact signed bytes، managed secret/key rotation، timestamp/replay tolerance وفق protocol، dedup/event uniqueness، bounded body ثم schema/resource validation.
- Signature checks قبل side effects؛ invalid signature event منقح، لا طباعة raw payload/signing secret/signature مادة حساسة.
- Webhook dispatch/processing/retry/idempotency تتبع T19/T25/T17 مع owning event semantics؛ لا generic credential replay حلًا لكل provider.
- Payment success لا تثبته browser redirect. Timeout/response loss لا تعني success/failure؛ provider-specific authoritative reconciliation قبل retry.
- Outbound provider calls تستخدم auth/TLS/bounds/cancellation/no secret logs وminimum PII؛ retry لا يكرر money/delivery effects بلا approved policy.
- Notification/invitation links من trusted base URL، purpose-bound/high-entropy/short-lived/revocable/one-use حسب use case. No secret query parameters وفق baseline؛ أي future token-link transport يحتاج explicit leakage-safe design قبل enabling.
- فتح link لا يمنح role/tenant access أو يعمل state change بGET؛ sensitive proof transport/confirmation صريحة.

---

# 23. Jobs / Events / Read Models / Caches

- Jobs لا تملك request actor طبيعيًا. Explicit tenant/execution authority/scope وفق T21/T09؛ null tenant ليست platform-wide access.
- User-initiated asynchronous work تحدد authorization-at-execution/data-download semantics وrevocation handling في owning workflow، لا replay obsolete user permissions من payload.
- Platform-wide jobs تحتاج explicit privileged operation وتقليل البيانات؛ لا default singleton current tenant.
- Messages/outbox/dead-letter store minimum facts/references؛ لا passwords/OTP/proof/private docs dumps. Failure payload redaction لازمة.
- Module contracts minimal provider-owned facts؛ no foreign DbContext/entities/raw HttpContext/full PII/security decision dump.
- Reports/search/cache تحمل tenant/user/security dimensions اللازمة وتطبق same visibility rules للsource data؛ read model/index/cache ليست bypass.
- Permission/session caches لا تنشأ داخل Feature قبل T29/invalidation model. Future caches لا تقبل stale authorization أو cross-user/tenant key collision.

---

# 24. Atomic Security Changes / TOCTOU / Commit Ambiguity

- Multi-row security mutation داخل owning Module تحفظ atomically وفق T15. لا credential issuance/success قبل Commit؛ follow-ups/outbox حسب owner.
- Critical uniqueness/single-use/attempt counters والTOTP/grant/ticket races تتبع T13/T16؛ DB constraints/atomic updates بالإضافة pre-check.
- Concurrency exception لا تصبح generic 409 أو allow؛ security race تصنف وفق T16/T32 مع confirmed rollback/outcome.
- Foreign fact read ثم local Commit لا تضمن cross-module atomicity. Invariant لا تتحمل تغير fact تحتاج ownership/workflow review، لا distributed lock/shared transaction عامة.
- **الاستثناء الوحيد المعتمد:** `RecordCashPayment` في T15 بين StudentFinance وBranchFinance على نفس PostgreSQL transaction، عبر participants مملوكة ومحدودة؛ Payment+CashMovement+idempotency/outbox المطلوبة atomic.
- هذا الاستثناء لا يتيح foreign DbContext أو arbitrary cross-module read/lock، ولا يشمل IdentityAccess ticket issuance. أي استثناء إضافي يحتاج قرارًا صريحًا يعدل T15.
- Locks/order/timeouts/retries كما في T16؛ لا network calls داخل DB transaction ولا blind retry لsecurity mutation.
- Commit ambiguity لا تعني rollback أو failed business outcome. T17 authoritative primary reconciliation ونفس intent/key/OperationId؛ no new financial effect أو generic secret replay.
- Current permission/visibility تعاد قبل result replay وفق القسم 11؛ no fresh assurance للCompleted فقط.

---

# 25. Security Time وExact Boundaries

- UTC security timestamps/expiry عبر Application IClock وفق T11؛ client timestamps لا تثبت auth time/freshness/event truth.
- `nowUtc >= ExpiresAtUtc` يعني expired حيث owning protocol يقرر؛ لا arbitrary grace.
- JWT ClockSkew فقط في JWT protocol المعتمد؛ لا تمد proof/OTP/grant/retention/business expiry صامتًا.
- Framework/DB clocks لا يتحكم فيها Application IClock تلقائيًا. Supported TimeProvider seams أو documented valid/expired scenarios وفق T33 بدون validation bypass.
- .NET ticks وPostgreSQL microsecond/JWT seconds boundaries تختبر بالprecision المناسبة؛ no impossible exact tick assertion بعد round-trip.
- No sleeps لتجاوز expiry؛ controlled clock/race seams. Limiter framework time source يختبر عبر supported seam أو policy-level boundary + actual HTTP wiring rejection، لا ادعاء fake clock يضبط middleware تلقائيًا.
- Security hot reload/rotation لا يخلق inconsistent security state؛ startup-bound/reloadable options تحسم في T36، مع review عند تغيير lifetimes/limits.

---

# 26. Errors / Security Events / Logs / Traces

## 26.1 Error boundary

- Expected failures تستخدم registered stable codes وsafe descriptions؛ لا raw DB/provider/serializer messages.
- Unexpected error = sanitized `500 General.UnexpectedError` وفق T32؛ correlationId إلزامية وtraceId فقط عند Activity.
- Authentication/authorization/binding/routing/rate-limit failures تستخدم writer مشتركة حيث الطلب وصل التطبيق، لا handlers-only envelope.
- `401` challenge و`405 Allow` و`406/413/415` و`429 Retry-After` تتبع T32. HEAD no body؛ response-started/cancellation لا تسبب overwrite أو synthetic 499.
- Infrastructure failure لا تزور inactive/not-found/permission denial، وclient disconnect قرب Commit لا تعني rollback؛ classification من T15–T18/T32.
- Public details/metadata لا تكشف resource مخفية أو sensitive provider/DB state؛ logs الداخلية أيضًا تخضع redaction.

## 26.2 Minimum event contract

Security event structured وتحتوي فقط metadata اللازمة:

```text
Stable event code + outcome + occurredAtUtc
Owning module / operation
Actor/account/session/institution/resource references when known and necessary
Current correlationId + trace reference when available
Safe reason category
Bounded source/risk metadata only when policy needs it
```

لا نضيف arbitrary request body أو exception dump أو permission snapshot. User-supplied text/header values bounded/encoded لمنع log injection، ولا تصبح metric dimensions.

| Event family | Minimum required evidence when implemented |
|---|---|
| Authentication | login success/failure؛ OTP/proof verification outcomes؛ MFA/recovery outcomes |
| Session/security reset | creation/revocation؛ refresh reuse؛ password/channel/MFA changes؛ suspension/closure |
| Access controls | denied authorization وsensitive cross-tenant/tamper attempts؛ no hidden data payload |
| Permission/support | role/scope/capability changes؛ exceptional access/break-glass activity |
| Abuse/integration | quota rejection aggregates؛ abnormal guessing/delivery patterns؛ invalid webhook signatures |
| Infrastructure controls | sanitized unexpected/security-control failures؛ startup misconfiguration without secret values |

- Full sensitive reads/ownership/approvals لها business audit/SensitiveAccessEvent حيث يتطلب البزنس؛ technical security log ليست immutable Audit Log.
- Never raw: password/hash/OTP/TOTP secret/recovery codes/proof/refresh/JWT/ticket handle/Authorization/Cookie/Set-Cookie/private key/provider secret/connection string.
- PII تقتصر على approved masked/internal references؛ plain hash لا يضمن anonymity لقيم قليلة الاحتمالات. No secret metrics labels/traces/baggage أو analytics.
- No full auth/PII/payment request-response body logging افتراضيًا. Raising debug level لا يعطل redaction.
- T34 تحدد sinks/retention/access/alerts/metrics/trace sampling؛ review redaction يشمل sink/exporter/interceptors الفعلية، لا message template وحدها.
- Abusive request floods لا تنشئ unbounded log work. Required committed audit events لا تسقط arbitrary sampling؛ aggregate/sampling للtelemetry المتكررة موثق ويحفظ ability to investigate.
- Logs/alerts ليست source of authorization ولا proof of wrongdoing. Auto-ban/suspend لا تنشأ من signal بلا approved policy.
- Logging failure ليست rollback evidence؛ mandatory transactional audit/outbox تفشل أو تنجح حسب owning business transaction، أما technical sink outage يعالج بـbounded diagnostics وفق T34، لا generic fail-open/fail-every-request rule.

---

# 27. Environment / Configuration / Test Helpers

- Dev/Test/Prod data/keys/provider credentials منفصلة. Test accounts/backdoors/debug endpoints لا تنشط Production.
- Unknown environment/missing critical options لا تتحول Development behavior؛ ValidateOnStart والfeature activation validation حيث relevant.
- Secrets خارج tracked appsettings/source/docs؛ key material لا يطبع عند startup validation failure.
- Missing Production signing key توقف startup وفق T13/T36؛ runtime-generated keys فقط في tests/local policy صريحة، لا production fallback.
- Development OTP/provider helpers narrow وenvironment-gated؛ real negative tests تثبت غياب exposure ورفض production enablement.
- Tests hooks لا تغير production security semantics أو authorization policies. Testing host overrides narrow adapters فقط وفق T33؛ no boolean/header MFA bypass أو fake current tenant/users كدليل أمن.
- No DeveloperExceptionPage/config/token/OTP/debug diagnostics endpoints في Production؛ public health minimal.
- Config/secret changes الحساسة لها owner/review/rotation plan؛ startup-bound/reloadable قرار صريح في T36.
- Service/DB/storage credentials أقل صلاحية. Routine direct Production DB edits ليست workflow؛ emergency path موثقة ومسجلة.

---

# 28. Dependencies / Build / Supply Chain

- Dependencies أقل عددًا، trusted/maintained، stable ومتوافقة مع T01؛ central versions/no floating package ranges.
- Security-relevant advisories تصنف exposure/severity مع owner/remediation/retest. Scan signal وليس guarantee أو auto-success.
- Secret scans/reviewed forbidden-pattern scans وفق T33؛ output path/count/classification، لا secret نفسه.
- No download-and-execute untrusted build/deploy tool بدون pin/verification/review.
- T38 تختار automation/SAST/SCA/secret-scanning/package-source controls وbuild artifact/provenance policies؛ لا نفترض vendor أو infrastructure enterprise جديدة.
- Analyzer/test/scan suppressions تحتاج scoped reason/owner/review؛ no critical skips أو blanket allowlist.
- Any discovered leaked secret تعامل compromised: remove from tracked exposure + revoke/rotate عند وجود live secret + safe incident handling؛ deletion وحدها لا تجعل secret آمنة.
- Code review/threat tests تبقى لازمة حتى لو dependency/static scans Green.

---

# 29. Sensitive Workflows / Support / Flags

- Operations تصنف Normal/Sensitive/HighlySensitive حسب identity/money/ownership/permission/privacy impact؛ requirements تحددها business/authorization owner.
- Ownership/control transfer ليست Role CRUD؛ permission + assurance + approval/conflict/audit workflow حسب البزنس.
- Financial operations تجمع current authorization/assurance/idempotency/concurrency/atomicity/audit/provider truth حيث تنطبق؛ no PrimaryOwner override.
- Support least privilege؛ no passwords/full identity docs/bank details افتراضيًا. PlatformSupport explicit case+grant scope/duration، لا generic tenant bypass.
- Break-glass إن نُفذ يحتاج reason/strong auth/limited scope+duration/audit+alert+review. Impersonation غير معتمدة افتراضيًا وتحتاج قرارًا مستقلًا؛ no silent masquerading.
- Manual recovery للحساب الحساس والPrimaryOwner تتبع Business 14/12، ولا customer-support shortcut بلا independent review حيث مطلوب.
- Feature flag availability فقط؛ disabled action تمنع server-side side effect ولا UI-only hiding. Flag لا تمنح permission أو تتجاوز approval.
- Institution creation/trial/subscription eligibility/invitations تتبع owning Business/Module contracts عند تنفيذها؛ لا temporary bypass أو weak predictable link tokens لتجاوز dependency ناقصة.
- Required MFA غير منفذة تعني أن sensitive operation غير Ready، لا Fake-MFA toggle أو طلب من client للتظاهر بالتحقق.

---

# 30. Threat Review / Security Exceptions

High-risk Feature تعمل mini threat review قبل التنفيذ؛ no huge model لكل CRUD عادي. Required areas: auth/recovery/MFA؛ access/tenant boundaries؛ permission/admin/support؛ money/ownership؛ uploads/imports؛ URL fetch/webhooks؛ sensitive exports.

Review مختصرة تغطي:

```text
Assets + classification + business impact
Actors + access paths + trust boundaries + entry points
Abuse / tamper / enumeration / replay / concurrency / resource-cost cases
Controls + owning decision/module + failure semantics
Configuration + secrets + deployment assumptions
Tests / verification evidence + residual risks / unresolved decisions
```

- Acceptance criteria تشمل negative/tamper/replay/deny/fault cases، لا happy path فقط.
- Risk signals لها retention/purpose/false-positive considerations؛ لا punitive auto-decisions بلا policy.
- Exception/deviation تسجل scope/risk/owner/compensating controls/review وexpiry/removal plan إن مؤقتة؛ لا «هنشيله بعدين» أو client bypass.
- Exception لا تُقر صامتًا في code/test expectation، ولا تلغي Business Rule؛ تغيير المعنى يرجع لقرارها المالك.
- Security blocker تمنع تفعيل الـFeature المتأثرة، لا توقف كل Features المستقبلية غير ذات الصلة.

---

# 31. Required Security Verification Matrix

T35 تحدد controls؛ T33 تحدد runner/isolation/real PostgreSQL/time/race/fault evidence. المصفوفة التالية تنطبق **عند تنفيذ المسار المعني**، وليست ادعاء اختبارات موجودة.

| Area | Minimum required verification |
|---|---|
| Endpoint inventory | Classification/owner/auth/policy registration؛ no accidental public/admin/debug exposure |
| Authentication | invalid/expired/wrong alg/typ/kid/signature/claims؛ revoked session/account/SecurityVersion؛ anti-enumeration |
| Browser | exact CORS؛ missing/wrong/null Origin؛ missing/wrong custom header؛ simple request rejection؛ cookie attributes/path/delete؛ no credential JSON/storage |
| Access paths | Staff/Student/Guardian/PlatformSupport cases عند implementation؛ actor/relationship/target mismatch؛ no merged/fallback permissions |
| Authorization | anonymous/invalid auth/hidden context/valid denied/authorized؛ function/object/property/scope/assurance cases |
| Mutable state | role/scope/capability/relationship/support grant/session revocation effective next request؛ no JWT/cache shortcut |
| Tenant | guessed IDs/route/body tampering؛ cross-tenant read/write/constraint cases؛ no context؛ parallel actor/tenant isolation |
| Request/output | unknown mutation/query fields؛ duplicate scalars؛ invalid enum؛ body/list/string/page bounds؛ DTO/PII/secret absence |
| Rate policies | required wiring؛ IP+identifier/target/user/tenant budgets؛ non-existing target safety؛ resend/cross-challenge limits؛ QueueLimit=0 |
| Rate HTTP | real limiter rejection uses 429/code/type/title/no-store/correlation؛ known Retry-After rounds up؛ no invented delay/remaining headers |
| Limiter safety | finite bucket/policy/dimension count؛ safe collisions/shared concurrency؛ spoofed IP/device/tenant keys؛ no unbounded allocations؛ runtime-scope limitation documented |
| Identity uniqueness | concurrent normalized email collision including unverified/non-null؛ phone uniqueness؛ no pre-check-only guarantee |
| Atomic security | proof/OTP/recovery/TOTP/grant/ticket single-use races؛ counters؛ rollback؛ Login vs Suspension/reset |
| Replay/transactions | first execution vs Completed with expired/consumed original ticket؛ current revoke denies replay؛ cash atomic exception؛ ambiguous Commit reconciliation |
| Errors/logging | no stack/SQL/secrets/PII leakage؛ malicious input/header log injection؛ actual captured sink/exporter redaction؛ expected events |
| Environment | missing keys/config fail closed؛ no production dev/test helpers؛ no fallback signing secrets |
| Conditional integrations | upload/scan/SSRF/signature/replay/provider/data-download controls قبل enabling |

## 31.1 Race / Time / Fault Evidence

- Security races تستخدم separate real DB connections/scopes وcontrolled async stages وفق T33، bounded waits/cleanup؛ no deadlocking barrier after every participant acquired lock.
- No retry-until-green/no expected critical skips. Original race failure تحفظ كdefect لا تخفى بتكرار النجاح.
- Pre-/post-Commit/response-loss tests تميز rollback عن durable committed outcome؛ authoritative lookup outage/recovery branch واضحة.
- Real HTTP pipeline تستخدم production auth registration وnarrow provider adapters؛ handler-only tests لا تثبت JWT/CORS/CSRF/tenant middleware.
- Real PostgreSQL required للconstraints/isolation/atomicity؛ no EF InMemory/SQLite كدليل security races.
- No impossible time precision claims أو assumption أن TestClock تغير كل framework clocks.
- Browser/edge/vendor-specific controls تحتاج evidence من surface المعنية؛ تستبقى limitation صريحة إذا لم تُشغّل بعد.

---

# 32. Feature / Release Gates وEvidence

- كل Feature تغير authn/authz/tenant/secrets/PII تنفذ required security regressions قبل اعتبارها Done.
- Release build + non-zero discovery + relevant Unit/Integration/Architecture suites كما في T33؛ no required critical skipped/failed tests أو hidden flakiness.
- Fresh migrations/pending-model/upgrade checks عند schema applicability، وفق T08/T33؛ لا تفرض migration proof بلا schema/code موجودة.
- Security scans وreviewed findings/config/redaction tests حسب implemented scope؛ scanner Green لا يغني عن actual path testing.
- Evidence تذكر actual commands/result/config versions/test totals/isolated DB/race repetitions ومراجعة exceptions/limitations، من غير credentials/payloads.
- Git diff/state checks hygiene **عند وجود Git repository**؛ لا mandatory clean Git/empty commit أو حذف unrelated user changes للحصول على Green.
- Passing document review لا يعني runtime security verified. لا تقرير build/test/migration/production readiness دون تنفيذ حقيقي.
- Demo/review تستخدم synthetic test data، ولا توفر permissive security shortcut للعرض.

---

# 33. Production Readiness / Backups / Incidents

متطلبات التشغيل تنطبق قبل Production deployment حقيقي حسب Business 14 وT34/T38/T39، ولا نفرض SIEM/WAF/paid pentest أو enterprise operations كشرط لتثبيت قرارات مشروع التخرج.

- Deployment review تشمل TLS/Host/proxy/CORS/cookie/config/key persistence/least privilege/limits/dependencies/storage/backup policies.
- Backup encryption/access/retention وrestore testing، وRPO/RTO من approved operating plan؛ backup لا تلغي privacy lifecycle.
- Incident runbook تحدد detection/containment/revocation/rotation/evidence preservation/recovery/review ومسؤوليات واضحة.
- Evidence لا تمحى أثناء remediation بلا retention/incident policy؛ no destructive auto-response لمجرد alert.
- Data-breach notification deadlines/legal obligations من Business/Legal approved policy؛ T35 لا تخترع مدة أو jurisdiction.
- قبل Production Release يلزم strong security review؛ independent penetration testing **يفضل عند الإمكان** وفق Business 14، وليس vendor إلزامية للعرض الأكاديمي أو لكل commit.
- Review/pentest scope عند production تركز auth/recovery/tenant/payments/files/admin/data access؛ findings لها owner/remediation/retest، لا إغلاق Critical بعبارة low probability بلا exposure analysis.
- Project/demo completion لا تعني جميع operational/production gates منفذة أو كل future Feature جاهزة.

---

# 34. Definition of Ready / Done

## 34.1 Sensitive Feature Ready

- Approved Business rule وAPI/error/security contracts؛ actors/access path/resources وclassification واضحة.
- Threat/abuse/replay/cost cases محددة، owners/typed contracts/atomic boundaries معروفة.
- Required assurance/approval/privacy rules محسومة؛ no temporary security bypass.
- Required secrets/config/limits/storage/provider dependencies قابلة للتنفيذ والتحقق بأمان قبل enabling.
- Negative/race/fault/HTTP/DB test plan مناسب مع documented deployment/time assumptions.
- أي blocker مؤثر على security semantics أو secret/PII safety يُحسم قبل تفعيل الـFeature.

## 34.2 Sensitive Feature Done

- Server-side controls الحالية مطبقة مع tenant/function/object/property checks حيث relevant.
- Implemented protocols تطابق T12–T18/T31/T32؛ no fresh-ticket requirement للCompleted replay أو generic secret replay.
- Config fail-closed وresource/partition bounds وrate contracts مثبتة عند الصلة.
- Required tests/scans/evidence من real relevant paths وفق T33؛ no critical skips/zero-discovery/fake evidence.
- Output/log/trace/event redaction وdata retention/access rules واضحة؛ approved exceptions/residual risks معلنة.
- Operational controls خارج test scope تسجل pending إلى hosting/provider verification، ولا توصف Verified.

## 34.3 T35 Decision Done

المبادئ والowners/access context/contracts/rate policies/ASVS scope/threat/test/release requirements محددة وغير متعارضة مع القرارات المالكة. هذه Definition of Done للقرار، لا claim أن التطبيق نفذها أو اجتازها.

---

# 35. ما لا يحسمه T35

Vendor/tool/store/topology choices التالية تبقى لقراراتها:

```text
Logging sinks / alert routing / dashboards / SIEM
Secret store / key vault / encryption library / Data Protection key-ring storage
Docker / CI runners / SAST-SCA vendors / artifact automation
Cloud / reverse proxy / HSTS termination / WAF-DDoS services
Coordinated multi-instance limiter storage and deployment-specific tuning
File storage / malware scanner / download serving topology
Payment provider / webhook protocol-specific parameters
Audit storage / job envelopes / caching / search implementation
Production operational RPO-RTO / independent pentest vendor and detailed cadence
```

هذه اختيارات تنفيذية، لا إعفاء من security outcome. Feature تحتاج إحداها لا تفعّل حتى توفر prerequisite آمنة. الحدود الأولية في T35 قابلة للضبط مركزيًا مع review؛ لا يوجد وعد بأن قيمة عالمية واحدة تناسب كل operation/provider/deployment.

---

# 36. القرار النهائي المختصر

> EduCenterOS تعتمد Secure by Design/Default وLeast Privilege وDefense in Depth وFail Closed. Inputs/IDs/headers/providers غير موثوقة، وكل وصول يحسم server-side من current authentication وtyped access path وfunction/object/property authorization وtenant scope. IInstitutionContext العامة تحتوي InstitutionId/UserAccountId/AccessKind فقط؛ لا generic relationship ID ولا دمج أو fallback بين أنواع الوصول.
>
> T12/T13 تملكان credentials/sessions/MFA، وT14 متطلبات الوصول/assurance، وT15–T18 atomicity/concurrency/idempotency/module contracts. Completed replay تعيد current access دون fresh step-up أو إعادة تنفيذ first-execution rules؛ secrets لا تدخل generic replay. Cross-module atomicity ليست عامة؛ استثناء RecordCashPayment المحدود فقط محفوظ.
>
> T31/T32 عقود HTTP المرجعية: explicit DTOs ورفض unknown mutation/query input وbounded validation وsafe ProblemDetails وno-store baseline. Browser tokens/cookies/Origin/CSRF تتبع T13 دون تخفيف. Rate-limit policies named/configurable ومقيدة state/cardinality؛ 429 بعقد موحد وآمن، مع توثيق per-instance scope وعدم تحويل infra outage إلى رفض business وهمي.
>
> Data classification/minimization/masking والmanaged standard cryptography وحماية secrets/logs/URLs/exports/Providers/Files/Jobs ملزمة عند انطباقها. ASVS 5.0.0 tailored subset وAPI Top 10 2023 مرجعان للتحقق، لا Certification. كل Feature حساسة تتطلب proportional threat review وreal negative/security evidence وفق T33؛ لا تنفيذ مبكر لFeature مستقبلية ولا claim اختبارات أو Production readiness بمجرد اعتماد الوثيقة.

---

## Appendix A — أمثلة التطبيق عند التنفيذ

الأمثلة ليست خطة زمنية أو Tasks مرقمة أو routes جديدة معتمدة؛ API contract الفعلية تثبت في الـFeature المعنية وفق T31.

| Feature عند تنفيذها | Required application of this policy |
|---|---|
| Email verification | normalization + non-null unique email قبل verification + bound OTP/proof + expiry/attempt/resend budgets + anti-enumeration + atomic state/race tests |
| Account facts provider | minimal provider-owned contract من T18؛ no PII/credential/session dump أو foreign authz decision؛ infra failure لا تزور inactive/not-found |
| Institution workspace discovery | current actor فقط؛ لا arbitrary userId لقراءة علاقات مستخدم آخر؛ revealable typed relationships وminimum response |
| Scoped branch/resource read | validated context + relevant access-path rules + 401/404/403/success matrix وفق visibility policy، وStaff BranchScope عندما تنطبق |
| Sensitive business mutation | current permission/visibility/assurance لأول تنفيذ + owning transaction + ticket/OperationId uniqueness + Completed replay بدون fresh ticket |
| Cash payment | approved T15 participants/atomicity + shift/drawer guards + T16 races + T17 commit ambiguity/replay |
| Upload/import/export/provider | لا activation قبل specialized storage/parser/quota/signature/reconciliation/privacy requirements واختبار المسار الفعلي |

---

## المراجع الخارجية

- [OWASP ASVS 5.0.0 — Scope, Levels and Tailoring](https://github.com/OWASP/ASVS/blob/v5.0.0/5.0/en/0x03-What-is-the-ASVS.md)
- [OWASP ASVS 5.0.0 — Versioned Requirement Inventory](https://github.com/OWASP/ASVS/blob/v5.0.0/5.0/docs_en/OWASP_Application_Security_Verification_Standard_5.0.0_en.csv)
- [OWASP ASVS 5.0.0 — Authorization](https://github.com/OWASP/ASVS/blob/v5.0.0/5.0/en/0x17-V8-Authorization.md)
- [OWASP ASVS 5.0.0 — Security Logging and Error Handling](https://github.com/OWASP/ASVS/blob/v5.0.0/5.0/en/0x25-V16-Security-Logging-and-Error-Handling.md)
- [OWASP API Security Top 10 — 2023](https://owasp.org/API-Security/editions/2023/en/0x11-t10/)
- [Microsoft — ASP.NET Core 10 Rate Limiting](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0)

المراجع تساعد verification/design؛ local Business/Technical owners تظل المرجعية للقواعد المعتمدة. هذه links للمصادر وليست packages أو tools جديدة مطلوب تثبيتها.
