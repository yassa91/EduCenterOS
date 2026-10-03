# T36 - Configuration & Secrets

> **2026-10-03 — Current development/testing override:** [T41 — Supabase Development & Testing](T41%20-%20Supabase%20Development%20%26%20Testing.md) replaces local PostgreSQL/Docker/Testcontainers provisioning and the synthetic-only infrastructure credential rule. The owner requested two cloud projects and an empty development database. The following earlier decision/evidence remains historical where it conflicts with T41. Remote compatibility verification is required before DB01 is complete.

## الهدف وحالة القرار

تثبيت عقد تحميل الإعدادات وإدارة الأسرار والمفاتيح في EduCenterOS، بحيث يكون مرجعًا للتنفيذ ولقرارات T37–T39، مع إبقاء Infisical خارج الـBusiness Modules.

هذه وثيقة **قرارات قبل التنفيذ** لمشروع تخرج، وليست تقريرًا عن Infrastructure منفذة أو اختبارات ناجحة. الـInventory أدناه مخطط؛ يصبح كل بند مطلوبًا عند تشغيل الـFeature المالكة له. لا توجد افتراضات عن Sprint بدأت، أو ملفات تشغيل/README موجودة بالفعل.

القرار المختصر:

```text
Infisical — authoritative real-secret source
    → explicit environment-bound bootstrap/runtime delivery
    → controlled IConfiguration composition
    → Typed Options + startup validation
    → owning Infrastructure services

No provider SDK in Domain / Features / Contracts / Handlers
No Infisical fetch per request
No secret fallback or permissive security fallback
Purpose-separated, versioned keys + protocol-aware rotation
Persistent, protected framework Data Protection key ring
Synthetic automated tests, independent of Infisical
```

---

## 1. الملكية والعلاقة بالقرارات الأخرى

- T01 تحدد .NET/ASP.NET Core والـFramework المعتمدين.
- T02–T05 تحدد Modular Monolith وApi Composition Root وdependency boundaries.
- T07/T08 تحدد PostgreSQL واحدة وschema/DbContext/migrations لكل Module.
- T09/T14 تحدد tenant/access isolation؛ authorization state ليست configuration.
- T11/T12/T13 تملك UTC وcredential/session/MFA/OTP protocol parameters.
- T15–T18 تملك transactions/concurrency/idempotency/module contracts، بما فيها استثناء `RecordCashPayment`.
- T31 تملك cursor purposes/format/version؛ T36 تملك Data Protection persistence/key lifecycle.
- T32/T34/T35 تملك error/logging/security contracts؛ T36 تمنع تسريب config/secrets عبرها.
- T33 تملك test suites وisolated PostgreSQL tests.
- T37/T38/T39 تحدد Docker/CI/Hosting mechanics طبقًا لهذا العقد، لا تعيد تعريفه ضمنيًا.

تغيير قيم policy الأمنية يتطلب مراجعة القرار المالك، وليس مجرد صلاحية تعديل Infisical.

---

## 2. Architecture Contract وCurrent Provider

العقد المعماري هو authoritative external secret source ثم runtime delivery ثم `IConfiguration` وvalidated services. **Infisical** هي الـprovider المعتمدة في Development/Staging/Production.

الـModules لا تعرف Project IDs أو Cloud region أو credentials الخاصة بـInfisical. لا SDK داخل Domain/Features/Contracts/Handlers، ولا lookup من global configuration داخل Use Cases. دمج الـprovider محصور في bootstrap/platform integration.

تغيير الـprovider يحتاج قرارًا صريحًا وخطة migration، لكنه لا يغير Business rules أو public module contracts. لا نضيف custom secret vault أو SDK client لكل Module أو network fetch لكل Request.

Data Protection key repository استثناء واضح لنوع التخزين: state يديرها الـFramework في persistent protected repository، وليست قائمة application secrets تُكتب يدويًا في Infisical؛ wrapping credentials الخاصة بها تبقى في Infisical.

---

## 3. تصنيف الإعدادات

| التصنيف | أمثلة | السياسة |
| --- | --- | --- |
| Secret | private keys، HMAC/encryption keys، DB passwords، provider tokens، private PFX | Infisical ثم protected runtime delivery؛ لا Git/logs/docs |
| Sensitive operational metadata | internal hosts، key-store paths، topology | deployment-scoped؛ لا نعتمد على سريتها كـcontrol ولا ننشرها بلا حاجة |
| Environment-specific non-secret | origins، issuer/audience، provider mode، public URLs | reviewed deployment config؛ validators أمنية |
| Stable non-secret defaults | approved lifetimes/counts/limits | يجوز source control؛ لا bypass لقراراتها المالكة |

Public RSA keys وkey IDs/versions وProject ID ليست credentials. Base64 ليست encryption. PFX التي تحتوي private key تظل Secret حتى لو password-protected.

Connection string تعامل Secret إذا احتوت credential؛ وبشكل عام لا تُطبع كاملة حتى إن استخدمت authentication خارجية.

---

## 4. المصادر المعتمدة ونسخة الحقيقة

- `appsettings.json` و`appsettings.{Environment}.json`: safe non-secret values فقط. Secret مطلوبة تكون غائبة، لا قيمة reusable أو default فعالة.
- `launchSettings.json` إن أُنشئت: non-secret developer settings فقط.
- Infisical: authoritative application-secret values مع owner/purpose/version metadata.
- Environment variables أو protected runtime mounts: delivery وليست مصدر حقيقة منافسًا.
- `.env`: ليست authoritative store؛ لا commit، ولا export دائم من Infisical كworkflow معتادة.
- User Secrets: explicit Development-only fallback حسب القسم 25، ليست trusted encrypted vault.
- CI secret facility: تحفظ bootstrap capability عند الحاجة، لا نسخة authoritative موازية لكل runtime secret.
- أي secret artifact مؤقت خارج Git، بصلاحيات ضيقة وعمر قصير وخطة cleanup؛ no raw values in shell history.

لا ciphertext-in-appsettings كاختصار بدل key-management design. لا أسرار داخل source أو migration snapshots أو release artifacts أو container layers.

---

## 5. اختيار البيئة قبل أي Secret Fetch

| ASP.NET environment | Infisical environment | المصدر |
| --- | --- | --- |
| `Development` | `dev` | explicit developer bootstrap |
| `Staging` | `staging` | staging machine identity |
| `Production` | `prod` | production machine identity |
| `Testing` | لا يوجد | isolated synthetic test composition |

- Bootstrap يتحقق من البيئة قبل authentication/fetch، ولا يعتمد على fallback ASP.NET الافتراضي إلى Production.
- missing/unknown environment أو mismatch مع Infisical يفشل قبل تحميل أي secret.
- إذا وُجدت `DOTNET_ENVIRONMENT` و`ASPNETCORE_ENVIRONMENT` يجب أن تتفقا؛ reject conflict بدل الاعتماد على اختلاف hosting precedence.
- canonical environment names أعلاه؛ لا aliases صامتة. Environment لا يُعاد تغييره بعد بناء الـHost.
- لا branch-name selection/grant لـprod، ولا default prod في tooling محلية.
- قيم dev/staging/prod مختلفة ووصولها منفصل. لا Production secrets/DB/providers في local أو automated tests.

---

## 6. Bootstrap وملف Infisical

إذا أُنشئت `.infisical.json` يجوز تتبعها فقط كـnon-secret locator metadata. لا token/client secret/private material فيها، ولا تعامل كمصدر authorization.

عقد كل bootstrap يتضمن environment، explicit project ID، secret path set، وtrusted endpoint:

- HTTPS endpoint مختار من allowlist تشغيلية موثوقة مستقلة عن repo-editable locator؛ تعديل `domain` في repo لا يوجه credentials إلى host غير معتمد.
- reject endpoint/region/project/environment/path mismatch. لا default endpoint صامت يتجاوز اختيار الاستضافة.
- Machine jobs تمرر Project/Environment/Paths صراحة، ولا تعتمد على developer login أو branch mapping أو locator defaults.
- pinning للCLI/adapter في T37/T38، مع smoke check لطريقة precedence/delivery الفعلية؛ لا نفترض flags أو merge behavior غير مختبرة.
- Infisical selectors تخص bootstrap فقط، وليست Business Options.
- أي URL/ID في diagnostics يكون allowlisted/non-secret؛ لا query tokens أو raw auth responses.

Project/workspace IDs لا تمنح access بذاتها. حتى locator آمنة ظاهريًا تحتاج مراجعة لأنها تحدد وجهة authentication.

---

## 7. Identities وSecret Zero

- Human developers يستخدمون identities فردية للوصول إلى dev، لا shared team login.
- Runtime وCI يستخدمان Machine/Workload Identities منفصلة حسب environment وjob boundary؛ لا personal developer token في CI.
- Runtime identity read-only للpaths اللازمة، بلا write/delete/admin.
- Staging لا تقرأ prod؛ PR/build jobs لا تحصل prod access.
- Production human access محدود ومراجع وauditable؛ temporary/approval-based عند توفرها، أو إجراء مكافئ موثق دون اشتراط paid feature.

Secret administration منفصلة عن runtime read access. Production secret changes لها authorized owner/reviewer وسجل تغيير/سبب/version/rotation evidence؛ نستخدم audit/versioning المتاحة في Infisical دون افتراض أن استرجاع provider version القديمة آمن كبروتوكول rollback.

الترتيب المعتمد لـmachine authentication:

1. Workload-native authentication عندما تدعمها الاستضافة وInfisical فعليًا.
2. Universal Auth كfallback، مع Client Secret في protected runtime/CI bootstrap facility خارج التطبيق.

Client ID non-secret؛ Client Secret وaccess token أسرار. Secret Zero لا يمكن حلها بافتراض أن credential الأولى موجودة فقط داخل Infisical.

---

## 8. Token TTL وBootstrap Credentials

عند Universal Auth نضبط Token TTL وMax TTL صراحة ولا نعتمد vendor defaults:

- one-shot startup bootstrap baseline: TTL = 15 minutes، Max TTL = 15 minutes، Period = 0، بدون unlimited periodic renewal.
- authentication/fetch retries bounded داخل نافذة bootstrap؛ فشلها يغلق التشغيل.
- Client Secret لها expiry/rotation موعد موثق ومتوافق مع job scheduling؛ لا unlimited lifetime افتراضية غير مراجعة.
- لو delivery تعتمد agent طويلة العمر، T39 توثق renewal/re-authentication/expiry/failure policy مستقلًا قبل تشغيلها، لا تمد TTL صامتًا.
- token/client secret لا تدخل raw arguments أو stdout أو traces.
- bootstrap يزيل credentials الخاصة بالمزود من environment الموروثة للـAPI child قدر الإمكان؛ التطبيق لا يحتاج حق الوصول للمزود بعد تحميل snapshot.

إلغاء Infisical token لا يمسح keys من process قائمة ولا يلغي application credentials تلقائيًا. Incident response تحدد consumer-side rotation/revocation وإعادة تشغيل processes المعنية.

---

## 9. Folder Scopes وRuntime Delivery

التقسيم المنطقي المبدئي:

```text
backend-api/shared
backend-api/identity-access
backend-api/institutions
backend-api/integrations/<provider>
```

T37–T39 تحدد layout الفعلية ومطابقتها لـRBAC؛ كل process تقرأ union الأسرار اللازمة للModules النشطة فقط. Folder separation ليست physical module isolation داخل monolith واحدة.

- أسماء flattened config فريدة عبر paths؛ duplicate canonical keys/case collisions تفشل، لا last-folder-wins.
- snapshot كاملة ومتسقة مع release config schema؛ لا partial fetch يعتبر نجاحًا.
- Delivery تستخدم environment variables canonical أو explicit read-only mounts/provider في Composition Root.
- المسارات المحددة فقط تقرأ؛ لا اكتشاف file/PEM/Base64 تلقائي من قيمة عشوائية، ولا arbitrary path loading.
- البيئة وfile permissions/process inspection/core dumps جزء من trust boundary؛ env injection ليست encryption.
- يُفضل launcher محدود الصلاحيات وsanitized child environment، دون إضافة custom secret distribution service.

---

## 10. .NET Precedence مقابل Effective Configuration Contract

الترتيب الافتراضي المعتاد للـapplication configuration، من الأعلى أولوية للأقل:

```text
Command-line arguments
Environment variables
Development User Secrets
appsettings.{Environment}.json
appsettings.json
Host configuration fallback
```

هذا behavior للـFramework، **ليس** security guarantee أن Infisical ستغلب كل المصادر. command line أو inherited env قد تغير critical settings لو تركناها unrestricted.

العقد المطلوب عند التنفيذ:

- Composition Root/launcher يعرف reserved critical namespaces: `ConnectionStrings`، `IdentityAccess`، `Platform:DataProtection`، `Platform:RateLimiting`، وإعدادات browser security.
- critical secrets تأتي من selected secret snapshot فقط؛ سياسات non-secret من reviewed release/deployment manifest، لا free-form argument overrides.
- reject raw arguments التي تستهدف reserved keys، وامنع inherited env/User Secrets من override أو إضافة critical entries خارج manifest المختارة.
- إزالة أو تجاوز sources الافتراضية للreserved sections عند composition؛ مجرد إضافة Infisical provider أخيرًا لا تكفي لإزالة lower-provider keys.
- يجوز operational args غير السرية غير المحجوزة من allowlist محدودة، دون bypass security validation.
- لا `AsEnumerable()`/`GetDebugView()` dumps لعرض effective config. diagnostics تعرض key name + violated rule فقط.

الImplementation يمكنها بناء filtered configuration root ثم إضافة authoritative complete critical snapshot؛ لا custom crypto أو provider SDK داخل Modules مطلوب لذلك. T37/T38 تثبت المسار المختار باختبارات precedence.

---

## 11. Critical Collections — Replacement لا Implicit Merge

.NET تدمج providers على مستوى keys، بما فيها array indices وdictionary entries. مصدر أعلى يحتوي قائمتين لا يمحو تلقائيًا عنصرًا ثالثًا من مصدر أقل.

لذلك `AllowedOrigins` و`ValidationPublicKeys` وكل key rings وrate policy collections وwrapping certificates **complete replacement snapshots**:

- لا critical collection entries في مصادر منخفضة غير selected manifest.
- bind من authoritative isolated section مع strict expected key set.
- deleted entry يجب ألا تظهر في effective result، حتى لو بقيت في inherited env أو local fallback قديمة.
- reject unknown/duplicate/case-colliding IDs، index gaps/ambiguous array inputs، malformed members وunexpected extra properties.
- لا نعالج missing secret باستكمالها من local stale copy.
- اختبارات removal/shorter-list/revoked-key تثبت النتيجة الفعلية، لا مجرد source order.

---

## 12. Typed Options وStartup Validation

`IConfiguration` تبقى في Composition/Infrastructure registration. Domain لا تعتمد عليها ولا على `IOptions`؛ Handlers لا تقرأ global env/config.

Options صغيرة حسب المسؤولية، تشمل عقود T13 مثل `AccessTokenOptions`، `SessionOptions`، `AccountLockoutOptions`، `MfaOptions` و`StepUpOptions`، بالإضافة OTP/key-ring/DP/rate-limit/database options.

- bind مرة واحدة، validate required fields + format + cross-field invariants باستخدام `ValidateOnStart` وvalidators.
- force validation لكل named options المستخدمة؛ عدم resolve خدمة معينة لا يؤجل اكتشاف missing key إلى أول Request.
- snapshot المستخدمة داخل services immutable/read-only عمليًا؛ `IOptions<T>` وحدها لا تجعل mutable object immutable.
- validate active dependency set قبل readiness؛ لا partial module startup.
- لا defaults/generated keys/localhost DB/wildcard origin/fake provider عند missing critical config.
- أخطاء validation لا تعرض actual value، full connection string، PEM، exception buffer أو secret length/sample.
- secret values لا تخضع لـsilent trimming/normalization؛ Base64 تُفك وفق encoding المتفق عليها، وinvalid/blank/placeholder material ترفض لا تُصلح تلقائيًا.
- length/format validation لا تثبت entropy؛ مفاتيح حقيقية تولد CSPRNG في provisioning لا passphrase أو deterministic seed.

---

## 13. Canonical Names وVersion IDs

`:` في IConfiguration تتحول إلى `__` في environment variables:

```text
IdentityAccess:Otp:CurrentHashKeyVersion
→ IdentityAccess__Otp__CurrentHashKeyVersion

IdentityAccess:Otp:HashKeys:<version>
→ IdentityAccess__Otp__HashKeys__<version>
```

- أسماء option sections التالية هي planned deployment contract؛ typed options classes لا يجب أن تطابق اسم section حرفيًا.
- نفس canonical key names تستخدم في dev/staging/prod مع values مختلفة؛ أمثلة connections المخططة: `ConnectionStrings:IdentityAccessDatabase` و`ConnectionStrings:InstitutionsDatabase`، لا aliases مختلفة لكل environment.
- internal key IDs/versions: lowercase ASCII وفق `[a-z][a-z0-9-]{0,63}`، immutable وغير مشتقة من secret.
- لا colon أو `__` داخل version ID؛ reject case collisions بسبب config case-insensitivity.
- نسخة جديدة تعني key material جديدة؛ لا overwrite قيمة نفس version وتفسد stored references.
- لا aliases مثل `JWT_SECRET` أو `OTP_KEY` بجانب canonical names.
- `IdentityAccess:Otp:HashingKey` القديمة تُستبدل بالعقد versioned أدناه؛ لا legacy silent fallback. لو أنشئت setup خارج الوثيقة بالفعل، تحتاج migration صريحة قبل التنفيذ.

---

## 14. Planned Core Inventory

هذه **متطلبات مخططة وليست قائمة أسرار منفذة**. Required يعني عند تشغيل owner/feature؛ لا ننشئ credentials لكل Module مستقبلية قبل الحاجة.

| Owner / classification | Canonical key | Format / required-default | Lifecycle / minimum test |
| --- | --- | --- | --- |
| Active module / Secret | `ConnectionStrings:<Module>Database` | valid PostgreSQL connection config؛ required لكل DbContext نشطة؛ no credential fallback | credential rotation؛ same physical DB + runtime permissions |
| StudentFinance + BranchFinance / Secret | `ConnectionStrings:RecordCashPaymentDatabase` | required عند تنفيذ cash exception؛ shared connection/principal | minimal participant grants؛ atomic integration test |
| IdentityAccess / Secret | `IdentityAccess:Jwt:PrivateKeyPem` | RSA private PEM، >=2048 bits؛ required JWT signing | prepublish/activate/retire؛ private/public match test |
| IdentityAccess / non-secret | `IdentityAccess:Jwt:CurrentKeyId`، `IdentityAccess:Jwt:ValidationPublicKeys:<kid>` | current kid + complete public PEM map؛ required | propagation/removal/old-token/rollback tests |
| IdentityAccess / Secret + non-secret version | `IdentityAccess:Otp:HashKeys:<version>`، `IdentityAccess:Otp:CurrentHashKeyVersion` | Base64 CSPRNG >=32 bytes لكل key؛ current member required | short-lived verification overlap؛ unknown version fails |
| IdentityAccess / Secret + non-secret version | `IdentityAccess:Mfa:Totp:EncryptionKeys:<version>`، `IdentityAccess:Mfa:Totp:CurrentEncryptionKeyVersion` | Base64 32-byte managed encryption keys؛ required MFA | decrypt-only old keys + re-encryption؛ tamper/restore tests |
| IdentityAccess / Secret + non-secret version | `IdentityAccess:Mfa:RecoveryCodes:HashKeys:<version>`، `IdentityAccess:Mfa:RecoveryCodes:CurrentHashKeyVersion` | Base64 CSPRNG >=32 bytes؛ required recovery codes | retain while live hashes reference version أو explicit revoke/regenerate |
| IdentityAccess / Secret + non-secret version | `IdentityAccess:StepUp:OperationTickets:HashKeys:<version>`، `IdentityAccess:StepUp:OperationTickets:CurrentHashKeyVersion` | Base64 CSPRNG >=32 bytes؛ required operation tickets | ticket expiry/revocation horizon؛ binding/version tests |
| Platform / Secret | `Platform:RateLimiting:PartitionDigestKey` | Base64 CSPRNG >=32 bytes؛ required keyed partition mapping T35 | controlled restart؛ budget-reset impact test |
| Platform / operational metadata | `Platform:DataProtection:ApplicationName`، `Platform:DataProtection:KeyRingPath` | fixed per-environment name + protected durable path؛ required T31 cursor protection | restart/shared-instance/isolation/permissions tests |
| Platform / non-secret selector | `Platform:DataProtection:CurrentWrappingCertificateId` | current certificate member ID؛ required | new wrapping activation + old-key readability |
| Platform / Secret | `Platform:DataProtection:WrappingCertificates:<id>:PfxBase64` | valid private certificate bundle؛ required current + retained decrypt certificates | private-key capability؛ persistence/re-wrap/restore tests |
| Platform / Secret, if needed | `Platform:DataProtection:WrappingCertificates:<id>:Password` | required only إذا PFX password-protected؛ no guessed password | import failure redacted؛ same certificate lifecycle |
| Integration owner / Secret | provider-specific section، مثل `Integrations:<Provider>:Credentials:...` | provider-defined؛ required فقط للprovider النشطة | provider rotation/revocation؛ synthetic test replacement |
| Bootstrap / Secret | Universal Auth client secret / access token | protected facility + explicit TTL؛ ليست Business Options | expiry/scope/sanitized-child smoke check |
| Policy owner / non-secret | issuer/audience/lifetimes، browser origins، MFA/StepUp/rate policies | reviewed complete policy؛ required validated effective values | bounds/cross-field/fail-closed tests |

كل key ring مستقلة بغرضها وببيئتها. لا shared OTP/Recovery/Ticket/TOTP/rate key، ولا reuse JWT private key كشهادة DP wrapping.

Refresh credential عالية الـentropy تخزن SHA-256 حسب T13؛ لا نضيف HMAC key بلا قرار مالك. StepUpGrant state ليست signing key جديدة. OTP proof/MFA handle تتبع storage contract المالك؛ إن أُضيف HMAC لغرض جديد يلزم inventory/version/lifecycle مستقل، لا reuse تلقائي.

---

## 15. Database Credentials وحدود T08/T15

كل named module connection تصف **نفس physical PostgreSQL database** طبقًا T08، مع schema/DbContext/migration-history ownership منفصلة. اختلاف المستخدم/password/search path لا يعني databases منفصلة؛ لا نتحقق بمساواة strings الكاملة.

- validate deployment database identity عبر normalized host/port/database/approved topology؛ server aliases/proxies تحتاج mapping موثقة، لا نكتفي بتشابه الأسماء.
- integration/deployment checks تثبت الواقع والpermissions، لا parsing وحدها.
- Runtime credentials أقل صلاحيات ممكنة؛ migration runner credential منفصلة تملك DDL اللازم، ولا تُحقن في API افتراضيًا.
- static database credentials هي baseline؛ dynamic leased secrets ليست مطلوبة قبل تصميم pool/renewal/reconnect/failure lifecycle.
- credential rotation تراعي connection pools/new connections/provider overlap وrestart/readiness؛ لا نفترض أن تغيير Infisical يبدل connections الجارية.
- design-time factories/migration commands تستخدم explicit dev/test config، بلا hardcoded/local password fallback أو raw credential arguments.

`RecordCashPayment` فقط لها shared connection وprincipal صالحان للtransaction الواحدة، بصلاحيات محدودة لparticipants StudentFinance/BranchFinance وowned idempotency/outbox المطلوبة حسب T15. لا cross-schema write grants لكل Module، ولا foreign DbContext writes. هذا لا يصنع physical security isolation داخل process واحدة، لكنه يحافظ على least-privilege connection paths.

---

## 16. JWT Key Contract

T13 هي owner للtoken protocol: RS256 only، approved typ/issuer/audience/kid validation، بلا remote key discovery أو algorithm fallback.

- `PrivateKeyPem` private RSA قابلة للتوقيع، وpublic map صالحة للتحقق؛ reject public-only signing key أو private material في public map.
- `CurrentKeyId` عضو موجود في map، وpublic key المطابقة له تطابق actual signing private key.
- RSA >=2048 bits؛ malformed PEM/duplicate key IDs/unsupported key type/empty map تفشل startup.
- PEM multiline تُنقل كما هي؛ لا blind Trim أو تحويل `\n` حرفية تلقائيًا إلى newline لإخفاء delivery bug.
- Current process توقع بمفتاح current فقط، وتتحقق بالمجموعة المحلية approved كاملة.
- لا runtime-generated JWT fallback في Development/Staging/Production. Test-generated keys فقط داخل isolated tests.
- public keys يجوز deployment/source control حسب approved lifecycle، لكنها ليست invitation لزيادة accepted keys بلا مراجعة.

---

## 17. JWT Planned Rotation وCompromise

الـplanned rotation تمر بثلاث مراحل منفصلة:

1. **Prepublish:** أضف public key الجديدة إلى complete validation set لكل validators، مع إبقاء signer القديمة. deploy/restart وتحقق من وصولها فعليًا للجميع.
2. **Activate:** بعدها فقط بدّل current kid/private signer إلى الجديدة. rolling deployment قد تترك signers قديمة مؤقتًا؛ validation set تقبل الاثنتين.
3. **Retire:** احتفظ public key القديمة حتى آخر إصدار ممكن بها + approved maximum access-token lifetime + configured ClockSkew، ومع انتهاء rollback window الموثقة. بعدها أزلها بتغيير كامل واختبار removal.

Rollback لا تُعيد signer حُذفت public key الخاصة بها؛ rollback release/config compatibility تُختبر مقدمًا. لا نحتفظ بالold private signer موزعة بلا حاجة؛ أي نسخة rollback محمية بنفس ضوابط secrets.

عند compromise:

- reject/revoke compromised signing kid لدى كل validators فورًا حسب incident plan، ولو أدى لرفض tokens قبل expiry.
- coordinate rollout/restart وإبطال sessions عند الحاجة حسب T13؛ لا اعتبار rolling propagation لحظية.
- لا تستخدم compromised key كrollback ولا تنتظر normal overlap.
- signing/verification revocation تختلف عن encryption-key removal التي قد تفقد data؛ راجع القسمين 19 و22.

---

## 18. HMAC Key Rings وHashKeyVersion

OTP وRecovery Codes وOperationAuthorizationTickets لها rings مستقلة: current version للإصدارات الجديدة + retained verification versions للقيم القديمة.

- HMAC-SHA-256 عبر framework primitives، decoded random key >=32 bytes، constant-time comparison.
- stored `HashKeyVersion`/`hash_key_version` تحدد key بعينها؛ unknown/retired version تفشل verification، بلا تجربة كل keys أو fallback للcurrent.
- version تُحفظ atomic مع hash؛ T12/T13 تملك binding/serialization/single-use rules.
- current version موجودة ولها material صحيحة؛ reject identical material عبر purposes/versions وenvironment provisioning يمنع reuse.
- prepublish version لكل issuers/verifiers قبل استخدامها؛ controlled activation/restart؛ لا live overwrite لنفس version.

الretirement حسب الغرض:

| الغرض | متى يمكن إزالة old verification key؟ |
| --- | --- |
| OTP | بعد stop issuance + expiry/revocation لكل challenges/proofs التي تعتمدها + إنهاء rollout/rollback dependencies |
| Operation ticket | بعد stop issuance + expiry/revocation لكل tickets المرتبطة + إنهاء deployment dependencies |
| Recovery codes | فقط بعد استهلاك/revocation/regeneration لكل unused live codes التي تشير إليها، حتى لو عمرها طويل |

Recovery hashes **لا يمكن إعادة HMAC لها بمفتاح جديد دون raw codes**. لا نجلب أو نخزن raw codes لتسهيل rotation؛ إما retain old verify-only key، أو invalidate generation القديمة ثم controlled regeneration حسب T13.

Rolling budgets/resend/cooldown لا تُصفّر بتغيير OTP key، ولا key retirement تمد lifetimes. Incident revoke لمفتاح HMAC تُبطل affected credentials مع recovery/notification خطة؛ لا bypass verification.

---

## 19. TOTP Encryption Key Lifecycle

TOTP secret recoverable حسب T13، لذلك encryption وليست hashing. الـmanaged key ring منفصلة، و`encryption_key_version` محفوظة مع ciphertext.

عقد التنفيذ المكمّل لـT13:

- authenticated encryption باستخدام `AesGcm` من .NET، AES-256 (32-byte key)، 12-byte CSPRNG nonce جديدة لكل encryption و16-byte tag؛ لا cryptography implementation يدويًا ولا nonce reuse.
- versioned envelope تحفظ format version/key version/nonce/tag/ciphertext؛ authenticated context تربط secret بهوية MFA method/account وغرض TOTP، دون trusted bindings من العميل.
- استخدام raw TOTP محدود داخل service المالكة للتحقق/re-encryption؛ لا logs/general queries/replay storage.
- current key للتشفير الجديد؛ old keys decrypt-only للrecords القديمة. missing version أو failed authentication لا يتحول إلى enrollment bypass.
- prepublish ثم activate ثم re-encrypt عبر controlled owner operation مع atomic update/concurrency checks؛ decrypt بمفتاح old وأعد encryption بـnew nonce/current version.
- لا تحذف old key قبل التأكد من zero live references، ومن restore/backup/rollback dependencies الموثقة. backup retention جزء من retirement، لا مجرد DB count.
- عند compromise، قد يلزم MFA re-enrollment/revocation؛ إعادة تشفير ciphertext وحدها لا تلغي secret ربما كُشفت. لا blind deletion يفقد قدرة استرجاع data.

هذا اختيار framework primitive لإكمال managed-key contract، لا تغيير TOTP RFC6238 parameters أو Session/MFA assurance rules.

---

## 20. Data Protection — Persistent Protected Repository

T31 تستخدم ASP.NET Core Data Protection للcursors. الـbaseline المعتمدة:

- persistent filesystem key ring في dedicated durable directory/volume خارج checkout والصورة؛ لا ephemeral container filesystem.
- local/demo: directory مخصصة ثابتة بين restarts. replicas لنفس environment/application: shared durable repository بنفس الـFramework-compatible concurrency/permissions؛ لا independent rings خلف نفس API.
- `SetApplicationName` ثابتة: `EduCenterOS/<Environment>/Api` باستخدام الاسم canonical من القسم 5. لا hostname/pod ID/build number داخل discriminator.
- dev/staging/prod لها repositories وwrapping material منفصلة. fixed application name ليست بديل ACL أو tenant/cursor binding.
- purpose/version للcursor تتبع T31؛ لا reuse protector عام لكل credential/payload.
- explicit at-rest key protection باستخدام `ProtectKeysWithCertificate` وretained `UnprotectKeysWithAnyCertificate` عند rotation. اختيار persistence path وحده **لا يضمن encryption at rest**.
- certificate private material في Infisical، محملة إلى process عبر runtime delivery أو protected mount. wrapping certificate مستقلة عن JWT signer.
- الحالية قابلة للاستخدام في protection ولها private-key access للفك؛ retained certificates صالحة للفك حتى انتهاء dependency عليها، ولا تحذف لمجرد انتهاء validity date.
- repository ACL تمنع arbitrary readers/writers؛ writer هو API runtime المحدد أو generator معتمد. صلاحية كتابة DP keys ليست صلاحية كتابة Infisical secrets.
- platform قد تستبدل filesystem repository بـFramework-supported durable store بقرار hosting موثق يحافظ على same-instance sharing/encryption/isolation/restore، لا custom XML-in-Infisical client.

لا نستخدم ephemeral default/user-profile heuristics كProduction design. لا نضع DP key files في Git أو logs، ولا نعطل automatic key generation بلا generator/lifecycle بديلة مكتملة.

---

## 21. Data Protection Rotation وWrapping Certificates

الـFramework تدير generation/activation/rotation/cache لDP keys؛ نستخدمها بدل custom key scheduler. الـdefault lifetime البالغ 90 يومًا مقبول للbaseline؛ تغييره reviewed platform configuration.

- native DP auto-generation ليست prohibited JWT signing-key fallback؛ هما بروتوكولان مختلفان.
- rotation/repository refresh الخاصة بـDP exception مقصودة لـstartup-bound application-options policy. لا Infisical fetch لكل request.
- expired DP key قد تظل لازمة لفك payload قديمة؛ expiry ليست إذنًا بحذفها.
- wrapper rotation: prepublish new + old decrypt certificates لكل instances، ثم activate current wrapping certificate للkeys الجديدة.
- old persisted DP keys قد تبقى wrapped بالشهادة القديمة؛ retain private decrypt certificate حتى re-wrap مثبتة أو زوال كل live/backup/rollback dependencies.
- DP key rotation وحدها لا تعيد wrapping ملفات keys القديمة تلقائيًا. لا نفترض أن تبديل current certificate جعل old certificate غير لازمة.
- أي re-wrap تستخدم supported audited tooling/API مع backup والتحقق، لا handwritten XML/cryptography patch.
- key-ring backups مشفرة ومقيدة مثل secrets؛ restore drill يثبت availability للrepository وwrapping credentials والتطبيق discriminator الصحيحة.

Readiness عند التنفيذ تختبر repository access وقدرة protection/unprotection بلا إظهار مادة keys. multi-instance smoke يثبت أن payload من instance تُقرأ في أخرى وبَعد restart.

---

## 22. Revocation وDeletion وData Loss

لا global قاعدة «احذف كل old keys فورًا»:

- JWT/HMAC compromise: تعطيل acceptance للcredentials المتأثرة حسب بروتوكولها، وإجبار إعادة authentication/recovery عند الحاجة.
- TOTP/data encryption compromise: assess leaked plaintext وإبطال affected MFA عند الحاجة، مع controlled re-encryption/restore plan قبل حذف decrypt material.
- DP compromise: revoke affected key IDs مع خطة لإبطال payload المتأثرة، ثم propagation لجميع instances. Framework caches تعني أن repository update وحدها ليست ضمان immediate fleet revocation؛ urgent response تُجدد/restart instances وتتأكد من rejection.
- DP key deletion غير قابلة لاسترجاع unprotect بدون backup/key material؛ revocation/deletion قرارات مختلفة. لا تفعيل override لإعادة قبول revoked keys في Requests.
- Secret loss ليست مناسبة لإنشاء replacement بنفس ID أو إسقاط verification أو إعادة حساب trusted state من client.
- incident قد يتطلب إزالة compromised backup أو إعادة حمايته بعد حفظ recoverability المسموحة؛ لا نسخ unsafe قديمة لمجرد rollback.

owner توثق affected consumers/data، cutover/containment، notifications، audit، rollback المسموحة وproof of retirement. لا fixed rotation calendar واحدة لكل أنواع keys.

---

## 23. Security Policy Options غير السرية

T36 تملك loading/validation وليس تخفيف protocols. canonical section bindings المخططة:

| Section | Owner / responsibility |
| --- | --- |
| `IdentityAccess:Jwt` | AccessTokenOptions؛ issuer/audience/current kid/public keys/lifetime/ClockSkew |
| `IdentityAccess:Session` | SessionOptions؛ idle/absolute lifetimes |
| `IdentityAccess:AccountLockout`، `IdentityAccess:LoginAbuseProtection` | lockout/login budgets حسب T13/T35 |
| `IdentityAccess:Otp` | OTP lifecycle/budgets + versioned key references حسب T12 |
| `IdentityAccess:RefreshCredential`، `IdentityAccess:BrowserRefreshCookie` | entropy/rotation/cookie contract حسب T13 |
| `IdentityAccess:Mfa`، `IdentityAccess:StepUp` | MFA/recovery/grants/tickets حسب T13 |
| `IdentityAccess:BrowserSecurity:AllowedOrigins` | exact reviewed browser origins حسب T13/T35 |
| `Platform:RateLimiting` | named T35 profiles/bounded partition mapping |
| `Platform:DataProtection` | repository/discriminator/wrapping policy |

- Access token initial lifetime = 10 minutes، idle = 30 days، absolute = 90 days حسب T13.
- ClockSkew تضبط صراحة؛ initial T36 baseline = 30 seconds لـJWT فقط، لا default مكتبة أوسع ولا skew لOTP/proofs/tickets/business expiry.
- TOTP = 30-second step، ±1 step، 6 digits وRFC6238 SHA-1 interoperability حسب T13؛ لا unrestricted protocol knobs.
- Recovery codes = 10/generation و>=128 random bits؛ refresh >=32 random bytes؛ ticket handle >=256 random bits، grants/ticket lifetimes حسب T13.
- OTP/proof lifetimes/attempts/cooldowns/bindings تظل من T12/T13؛ لا إعادة تعريف أرقام مختلفة هنا.
- rate profiles/rolling windows/durable counters/finite buckets/queue behavior كما T35؛ positive bounded limits ولا “0 = unlimited” أو unknown disabled profiles.
- exact approved origins وcookie `Secure`/`HttpOnly`/`SameSite=Strict`/host-only/narrow auth path/approved name؛ لا wildcard credentialed CORS أو fallback يسمح بمصدر غير معروف.
- `X-EduCenterOS-CSRF: 1` هي non-secret fixed marker حسب T35، لا secret جديدة ولا بديل origin checks.
- optional integration = explicit approved disabled mode فقط؛ missing credentials للprovider enabled تفشل، لا fake Development adapter في Production.

Tenant IDs، memberships، roles، branch scopes، capability/access state وguardian/support relationships authoritative owner DB state حسب T14. لا `AllowAllTenants` أو `BypassTenantIsolation` أو security-off flags للتسهيل في tests.

---

## 24. Startup-bound، Reload وProvider Outage

Application secrets وcritical policy snapshots ثابتة طوال process lifetime؛ لا `IOptionsMonitor`/`IOptionsSnapshot` أو CLI watch لها افتراضيًا.

- baseline updates = validated config release + restart/controlled rolling deployment؛ consumers تدعم overlap الموضحة قبل activate.
- live reload مستقبلًا يحتاج قرارًا مع atomic publication/complete snapshot/rollback/old-value lifecycle وتغطية اختبارات، لا file watcher بمجرده.
- non-critical reload يمكن اعتماده منفصلًا إذا لا يغير authz/crypto/tenant/provider safety.
- DP native key-ring refresh والrotation هي الـexception الموثقة بالقسم 21، لا generic hot reload للأسرار.

Infisical unavailable عند startup: bounded retry ثم fail closed، بلا stale cached/local fallback صامت. readiness لا تقول healthy إذا snapshot ناقصة.

بعد startup: application تستمر بالقيم المحملة طالما credentials صالحة؛ provider outage وحدها لا تكسر كل Request. secret/token revocation أو DB/provider expiry قد تتطلب consumer action. classified infrastructure failure تتبع T32، لا account-inactive مزيفة أو security bypass.

لا custom persistent application secret cache أو local vault إضافية. الـvalidated in-memory startup snapshot ليست cache تتجاوز bootstrap validation. Staging تطبق نفس security validators الخاصة بـProduction، وDevelopment-only diagnostic/adapters لا تسجل فيها أو في Production.

---

## 25. Development وExplicit Offline Fallback

المسار المفضل: developer identity → explicit dev/environment/project/path → Infisical runtime injection → validated API.

- local Infisical outage لا تؤثر على Unit/Architecture أو isolated Integration tests.
- dev API التي تحتاج real dev secrets تتوقف، إلا إذا اختير **Development-only `UserSecretsFallback` bootstrap mode** صراحة.
- fallback مصدر كامل بديل، لا merge بقايا مع Infisical؛ يعطل dev real-secret fetch ويظل validator/format/version contract نفسها.
- لا fallback تلقائي بعد failed Infisical authentication/fetch. لا Production/Staging material داخل User Secrets.
- User Secrets خارج repo لكنها ليست مشفرة/trusted production vault؛ استخدامها local compatibility فقط مع تقليل العمر والنسخ.
- launcher/composition يزيل User Secrets provider الافتراضية في Infisical mode وفي Testing/Staging/Production، حتى لا تتسرب entries ناقصة.
- offline keys إن provisioned تكون dev-only ومولدة CSPRNG عبر explicit provisioning، لا API startup fallback ولا shared team key منشورة.

هذا لا ينشئ vault منافسة: Infisical تظل المصدر الطبيعي للreal secrets، والfallback المحلية استثناء opt-in محدود وقابل للإزالة.

---

## 26. Rate-limiter Digest Key وPurpose Separation

`PartitionDigestKey` تستخدم فقط keyed digest للnormalized internal partition identity وفق T35، ثم finite bucket mapping؛ ليست authorization credential ولا per-user lockout key.

- مستقلة عن OTP/recovery/ticket/JWT/TOTP/DP wrapping keys.
- stable داخل environment أثناء release وموحدة للinstances التي تستخدم policy نفسها، لكن limiter in-process يبقى per-instance حسب T35.
- تغييرها يغير bucket assignment؛ controlled deployment توثق أثر reset/collisions/overlap ولا تصفها كتدوير بلا behavioral effect.
- لا تعطل durable account/target attempt budgets المملوكة لـIdentityAccess، ولا reset counters عبر credential-key rotation.
- بعد digest-key compromise قد يلزم emergency rotate رغم reset أثرها، مع monitoring/provider quotas وdurable controls.
- لا raw phone/email/IP/account IDs في public partition headers أو errors/log labels، ولا key material diagnostics.

لا Redis/WAF أو distributed limiter مطلوب للمشروع لمجرد T36؛ زيادة عدد instances وتوحيد fleet budgets تحتاج قرار تشغيل منفصلًا وفق T35.

---

## 27. Future Integrations وSecret Ownership

كل secret جديدة تسجل owner/classification/canonical key/format/source/scope/required condition/default/validation/lifecycle/tests.

- SMS/email/object storage/webhook/provider secrets تضيفها Integration المالكة عند first implemented use case، لا credentials وهمية لكل provider ممكن.
- webhook signing secret منفصلة عن API credential ولو نفس vendor.
- recoverable sensitive fields غير TOTP تحتاج field-specific authenticated encryption/key contract قبل التنفيذ، لا استعمال OTP key أو DP cursor protector عامة.
- `IAccountFactsReader` وauthorization readers لا تكتسب keys جديدة بلا سبب.
- لا duplicate secret لكل Handler؛ typed infrastructure service واحدة داخل owner تستهلك الـOptions المطلوبة.
- description/tags تشرح purpose/version/rotation بدون value/PII.
- إن paid provider feature غير متاحة، equivalent control موثق أو توقف نشر feature المعتمدة عليها؛ لا silently drop invariant.

Provider/API credentials rotation تتبع vendor semantics: إن سمحت overlapping credentials، provision new → validate consumer cutover/restart → revoke old بعد توقف الاعتماد عليها؛ إن لم تسمح، maintenance/cutover plan موثقة. Bootstrap Machine Identity credentials تدور كذلك مع proof أن جميع launchers تستطيع authentication بالجديدة، ثم revoke القديمة؛ لا application-request fallback إلى revoked credential.

---

## 28. Configuration Schema Changes وRollback

config rename/delete أو إضافة required secret deployment contract change:

1. أعلن schema/owner/consumer changes وcompatibility window.
2. provision explicit new entries وdeploy compatible release، دون ambiguous aliases.
3. validate complete effective snapshot، ثم cutover.
4. remove obsolete config/key material فقط بعد live/rollback/backup dependencies حسب النوع.

`add → compatible deploy → verify → remove` لا يعني إبقاء old accepted JWT key أو old origin إلى الأبد. critical collections replacement دائمًا، وcompromise تتجاوز normal rollback window.

نروّج schema/policy expectations من dev إلى staging إلى prod، **لا secret values**. snapshot consistent مع code release؛ rollback لا تسترجع security-revoked key أو تعمل overwrite لنفس key version.

لا تكفي «Infisical updated»: gate تثبت كل required running consumers وتوافق validation/decryption قبل activation/retirement.

---

## 29. Docker، CI،Build وMigration Boundaries

T37–T39 تختار mechanics، مع هذه invariants:

- immutable build/release/image بلا runtime secrets أو key rings؛ لا Docker `ARG`/`ENV` secrets أو COPY ثم delete في layer لاحقة.
- build-time feed credentials إن احتجناها تدخل platform-supported temporary secret mechanism، لا baked artifact.
- PR/compilation/Unit/Architecture jobs بلا real runtime secrets؛ Integration تستخدم disposable PostgreSQL + synthetic config طبقًا T33.
- deployment job فقط لها needed environment/path capability؛ separate from runtime identity وبleast privilege.
- read-only credential mounts بصلاحيات ضيقة؛ DP repository durable writable بالقدر اللازم ومفصولة عن credential mounts.
- لا raw secret في process arguments/history/CI echo/serialized job output.
- no automated secret export artifact/caches؛ sanitize inherited bootstrap env.
- migration runner لها scoped DDL credential منفصلة، وتتحقق أنها نفس target DB المطلوبة. API لا تستلمها افتراضيًا.
- CLI/version pinning وprovider auth/sync اختبارات تشغيل منفصلة قبل أول deployment؛ no assumption أنها مجهزة الآن.

---

## 30. Redaction،Diagnostics وHealth

- لا secrets أو raw password/hash/OTP/proof/JWT/refresh/TOTP/recovery/ticket handles أو Authorization/Cookie/Set-Cookie/connection string في logs/traces/errors/metrics.
- failure message: canonical key name + safe rule/reason code؛ لا actual value أو inner exception تحمل PEM/request/auth buffer.
- `GetDebugView`/configuration dumps وprovider raw payloads ممنوعة في operational diagnostics.
- health/readiness تعرض minimal status للتشغيل ولا public provider topology/key inventory. private diagnostics مقيدة دون credentials.
- key IDs/versions قد تكون safe audit metadata بعد minimization، لا tags بمادة secret أو PII.
- لا generic Configuration API تعرض effective options أو تعمل secret read/write للمستخدمين؛ runtime لا تُعدّل Infisical secrets عبر Feature عادية.
- reports/screenshots/examples تُراجع وتُredact؛ placeholders فقط مثل `<PRIVATE_RSA_PEM>` و`<BASE64_RANDOM_KEY>`.
- لا chat/email/ticket sharing للraw production secrets؛ authorized provider access أو approved expiring sharing إذا متاح.

وجود key داخل memory ضرورة للوظيفة وليس إذنًا لdump؛ runtime host/permissions/core-dump policy جزء من T39.

---

## 31. Leak Response وSecret Scanning

أي leak في Git/history/logs/artifact/chat تعامل exposure محتملة:

- contain exposure، assess consumers/data، rotate/revoke وفق key purpose، ولا تكتفي بحذف النص أو redaction بعد النشر.
- Git history cleanup حسب الحاجة بعد revocation؛ لا destructive history rewrite تلقائية بلا تنسيق.
- scans على source/config/docs/tests/migration artifacts وCI artifacts، مع redacted findings لا secret stdout.
- patterns تشمل private PEM/PFX، credential-bearing connection strings، HMAC/encryption material، Infisical client/token وprovider secrets.
- synthetic test keys تُولد runtime فلا private blocks committed للاختبارات.
- Git-specific checks عند وجود Git repository؛ لا ندعي clean tree/scan Green دون evidence.
- scanner لا يثبت عدم وجود أي secret؛ review/provisioning/least privilege تظل لازمة.

---

## 32. Automated Verification Contract

كل الاختبارات التالية **متطلبات عند التنفيذ، ليست نتائج منفذة الآن**. Backend suites مستقلة عن Infisical وdeveloper login/User Secrets طبقًا T33.

| محور | الاختبارات الدنيا المطلوبة |
| --- | --- |
| Bootstrap binding | missing/unknown/conflicting environment؛ wrong project/path/endpoint؛ Production selection explicit؛ zero real fetch في Testing |
| Effective precedence | reserved command-line override مرفوض؛ inherited env/User Secrets لا تضيف critical key؛ full snapshot source فقط |
| Collections | shorter origins list لا تبقي old member؛ removal لكid/HMAC/cert version حقيقية؛ duplicate IDs/case collisions/unknown properties ترفض |
| Startup | required missing/malformed secrets؛ كل named options validated؛ no generated/localhost/wildcard/fake-provider fallback |
| JWT | malformed/private-only/public-only inputs؛ RSA size؛ kid mismatch؛ prepublish-before-sign؛ old token horizon + ClockSkew؛ retirement/compromise/rollback |
| HMAC | independent purpose keys؛ unknown version fails؛ old live OTP/ticket verification؛ short expiry boundaries؛ recovery key لا retire مع live hashes |
| TOTP | encryption/decryption عبر version؛ nonce/tag/envelope tamper/incorrect binding fails؛ old decrypt + re-encrypt؛ concurrency؛ no plaintext logs |
| Data Protection | protected durable persistence؛ restart/shared-instance unprotect؛ environment/application/purpose isolation؛ wrapper rollover/restore؛ revocation propagation |
| Policies | Session/MFA/StepUp/cookie/origins/rate bounds وcross-field validation؛ no security-off flags؛ rate key rotation لا يمحو durable budgets |
| Database | active module targets نفس DB مع credentials مختلفة؛ runtime/migration least privilege؛ cash shared principal grants/atomicity حسب T15 |
| Redaction/outage | safe startup failures/logs/errors؛ unavailable bootstrap fail closed؛ running config ثابتة؛ enabled-provider credential failure لا fake fallback |
| Architecture | no provider SDK in Domain/Features/Contracts/Handlers؛ no scattered env reads؛ no critical hot reload غير معتمدة |

- keys/certificates/HMAC material generated runtime synthetic؛ no real provider values.
- Production-like startup validators تُختبر داخل trusted isolated harness مع synthetic injection، دون public bypass option أو real Production fetch.
- PostgreSQL test target/policies تتبع T33؛ لا connection string من developer machine.
- expiry tests precision-aware: JWT seconds/ClockSkew منفصل عن DB/time boundaries للOTP/ticket.
- integration smoke لـInfisical RBAC/connectivity/CLI/mount permissions منفصلة في deployment pipeline؛ ليست dependency للBackend green suite.
- platform tests تثبت token TTL/secret scopes/no bootstrap credential inheritance حيث المسار المختار يدعم ذلك.
- لا entropy statistical test تدعي إثبات randomness من key length؛ نفحص generation primitive/provisioning path ورفض known placeholder formats.

---

## 33. Implementation وDeployment Readiness Gates

قبل تفعيل Feature/config جديدة:

- inventory كاملة وفق القسم 14، owner/key format/version وrequired condition معروفة.
- options/composition filtering/validators وsynthetic tests منفذة.
- provisioning/least privilege/path/source موثقة دون secret values.
- controlled rotation/retirement/rollback وdata restore أثرها واضحة.
- redaction وscan findings reviewed.
- enabled feature لا تصل readiness قبل كل dependencies المطلوبة.

قبل أول shared runtime/deployment:

- T37–T39 تحدد trusted Infisical endpoint، explicit project/path/env selectors، pinned tooling وطريقة delivery.
- machine authentication/Secret Zero/TTL/RBAC checked actual capabilities.
- DP durable protected repository + shared-instance/restore smoke مكتملة.
- JWT prepublish gate وخطة incident propagation موجودتان.
- DB identity/grants/migration/cash exception paths validated.
- docs/README وruntime locator إن أُنشئت تعرض Architecture/Infisical/UserSecretsFallback boundaries بلا claims أو credentials.

اعتماد الوثيقة **لا يعني** أن environment مجهزة أو feature منفذة، ولا نضيف مفاتيح مستقبلية فقط لإغلاق checklist ورقية.

---

## 34. ما يظل لقرارات التشغيل القادمة

ليست مطلوبة لbaseline المشروع إلا إذا hosting/workflow احتاجتها:

- Infisical Cloud vs self-hosted والregion/endpoint الفعلية.
- final CLI version وDocker entrypoint/CI vendor.
- native workload auth method أو agent/operator طويلة العمر.
- final shared filesystem/durable-store topology، backup retention وrestore tooling.
- dynamic DB secrets/leases أو KMS/HSM alternative.
- advanced paid approvals/sync features أو distributed rate limits.

هذه deployment choices، لا فجوات تسمح بإسقاط environment binding أو at-rest protection أو key-retirement contract. كل مورد يُحسم ويُختبر **قبل** تفعيل runtime التي تعتمد عليه.

---

## 35. حدود القرار والممنوعات

ممنوع:

- real secrets في Git/appsettings/docs/tests/images/logs، أو bootstrap auth raw arguments.
- process تعتمد network secret fetch لكل Request، أو SDK في Business Modules.
- stale lower-provider entry تعيد key/origin محذوفة، أو fallback تعوض missing production secret.
- same key لpurposes/environments مختلفة، أو overwrite key version مستخدمة.
- JWT signing قبل prepublish complete، أو rollback إلى compromised signer.
- حذف recovery verification key مع live codes، أو encryption/DP decrypt key دون restore/retirement proof.
- credentials لكل Module تشير إلى physical DB مختلفة، أو global cross-schema write principal كاختصار لاستثناء cash.
- config تنقل mutable authz/tenant state من authoritative owner DB إلى secret provider.
- اعتماد خطة Provider paid feature بلا تحقق، أو الادعاء أن tests/deployment/scans نجحت قبل تنفيذها.

لا custom cryptography، ولا بنية تشغيل ثقيلة أو tools إضافية لمجرد زيادة عدد controls.

---

## 36. القرار النهائي

EduCenterOS تعتمد Infisical كمصدر الأسرار الحقيقي، مع provider-agnostic application architecture وexplicit environment-bound bootstrap. تحميل critical configuration يتم من complete authoritative snapshots، مع منع command-line/inherited-env/stale collection overrides، وTyped Options/ValidateOnStart وفشل مغلق دون secret أو security fallback.

OTP/Recovery/Tickets لها independent versioned HMAC rings، وTOTP لها managed authenticated encryption ring. JWT rotation تُسبق بنشر public validation key للجميع، وretirement تراعي token lifetime/ClockSkew/rollback. long-lived recovery hashes وencrypted data لا تُعامل مثل short-lived credentials.

Data Protection لها persistent environment-isolated shared repository وexplicit certificate-based at-rest protection، وتستخدم native framework key lifecycle دون خلطها بمنع JWT fallback. database connections تحافظ على T08 database الواحدة وT15 cash exception الضيقة.

هذا عقد جاهز للبناء عليه، لا شهادة تنفيذ. التطبيق والاختبارات والruntime setup المقبلة يجب أن تقدم evidence للgates السابقة، مع keeping the graduation-project scope بسيطًا ودون اختراع security infrastructure موازية.

### مراجع رسمية للتنفيذ

المراجع توضّح Framework/provider behavior؛ سياسات المشروع الأكثر تقييدًا أعلاه هي قراراتنا، لا ادعاء أن vendor تنفذها تلقائيًا.

- [ASP.NET Core configuration providers and precedence](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-10.0)
- [.NET Options validation](https://learn.microsoft.com/en-us/dotnet/core/extensions/options)
- [Development User Secrets limitations](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-10.0)
- [Data Protection persistence, application isolation and certificate protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0)
- [Data Protection key lifecycle and caching](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-management?view=aspnetcore-10.0)
- [.NET AesGcm authenticated encryption primitive](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm?view=net-10.0)
- [Infisical Universal Auth and token lifetime controls](https://infisical.com/docs/documentation/platform/identities/universal-auth)
- [Infisical project configuration and endpoint/environment selection](https://infisical.com/docs/cli/project-config)


## S02 active local inventory

S02 uses snapshot schema 2 with the RuntimeProbeDatabase and IdentityAccessDatabase connections, versioned `IdentityAccess__Otp__HashKeys__v1`, current version, and independent `Platform__RateLimiting__PartitionDigestKey`. All numeric registration policy fields are captured explicitly from `infra/registration-policy.json` by the trusted launcher, without ambient overrides. Only active keys are required; no JWT/MFA keys are provisioned.

The separate `/backend-api/identity-access` dev bootstrap bundle includes runtime/migration passwords and named connections. Migration secrets are excluded from API snapshots and argv. The dedicated migrator validates actual dev database/user/PostgreSQL/schema ownership before DDL. `rate_key_binding` stores only the digest-key fingerprint to reject unsafe replacement that would reset persistent budgets; key transition is explicit, with no silent reset.
