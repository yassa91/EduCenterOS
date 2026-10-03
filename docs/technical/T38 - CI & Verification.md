# T38 - CI & Verification

> **2026-10-03 — Cloud test override:** [T41](T41%20-%20Supabase%20Development%20%26%20Testing.md) removes Docker/Testcontainers acquisition and supplies only restricted Testing-project credentials to the integration step. Infisical remains authoritative; CI uses a derived test-only GitHub secret. Earlier synthetic/container provisioning contracts below are historical where they conflict.

## القرار وحالته

```text
Status: Approved implementation baseline
Scope: GitHub Actions verification for S01 and active Features
```

نعتمد GitHub Actions وGitHub-hosted `ubuntu-24.04` runner، وRelease build وxUnit v3/VSTest verification وفق T33. CI للفحص فقط؛ النشر وProduction credentials خارج هذا القرار والسبرنت.

## 1. المرجعية

T01 تملك SDK/packages، وT33 تملك مستوى الاختبارات والـrunner والـcoverage المطلوبة وevidence، وT35/T36 تملك security/config/secrets، وT37 تملك runtime/test provisioning، وT40 تملك branch/review/merge authorization.

## 2. Workflow contract

- ملف `.github/workflows/ci.yml` باسم workflow `CI`، وjob ثابتة باسم `verify`.
- events: `pull_request` إلى `main` و`push` على `main`، و`workflow_dispatch` للفحص اليدوي عند الحاجة.
- لا path filtering يسقط Required Check على documentation-only PR؛ فحص tree الحالية يظل متاحًا لكل PR بعد تجهيز workflow.
- default `permissions: contents: read`، بلا write/package/deployment/id-token grants غير مطلوبة.
- PR code تفحص في `pull_request` context؛ لا تشغيل untrusted PR code عبر `pull_request_target` مع credentials/write access.
- concurrency تلغي runs القديمة لنفس PR؛ main verification لا تلغى لمجرد وجود PR مختلفة. Job timeout = 20 minutes كحد تشغيل أولي، لا SLA للمنتج.

## 3. Dependencies والتثبيت

`actions/checkout` و`actions/setup-dotnet` و`actions/upload-artifact` مثبتة على full commit SHA من official action repositories، مع version comment للمراجعة. توثق SHA الفعلية وقت تجهيز workflow؛ لا tag-only أو arbitrary third-party install script.

setup-dotnet تقرأ `global.json`، وتثبت SDK المعتمدة بدل الاعتماد على runner preinstall. package versions محددة مركزيًا وlock files تتبع في Git عند تفعيل locked restore؛ لا `latest`/Preview أو dependency update غير مراجعة.

PostgreSQL 18 image/digest وTestcontainers version نفس T37. لا service DB مشتركة بين jobs ولا dev runtime secret injection. Testcontainers تجهز Docker-owned test resources داخل runner.

## 4. الفحوصات المطلوبة

```text
Checkout
→ setup pinned SDK
→ dotnet restore EduCenterOS.sln
→ dotnet build EduCenterOS.sln -c Release --no-restore
→ dotnet test EduCenterOS.sln -c Release --no-build
→ validate actual test discovery/results
→ retain safe test reports
```

عند تفعيل lock files يستخدم restore `--locked-mode`. لا full-suite repetitions بلا تغير أو finding، ولا retries تمحو failures.

- Build: zero errors وzero unjustified warnings وفق T33.
- الاختبارات: Failed = 0، وRequired critical skipped = 0، وexecuted tests > 0 لكل implemented required suite.
- discovery/report guard تفشل لو suite المطلوبة بلا نتائج/اختبارات؛ exit code صفر وحدها لا تكفي.
- Unit/Integration/Architecture تفحص behavior والقواعد الموجودة فقط؛ لا tests تعيد implementation أو bypass للأمان.
- Integration تعتمد PostgreSQL الحقيقية؛ missing Docker/image/config prerequisite تفشل بوضوح.
- `git diff --check` ضمن hygiene حين توجد Git، ولا تعتبر بديلًا لفحوصات السلوك.

SDK/runner compatibility وfilter/reporting تثبت عند إضافة test projects. S01-T03/T04 لا تدعي تشغيل suite غير موجودة بعد؛ تجهيز workflow الكاملة في S01-T06.

## 5. Isolation والأسرار

build/PR/test jobs لا تحمل Infisical access أو dev/staging/prod secrets. Test credentials random/synthetic وتعيش داخل fixture/runner، ولا تطبع connection strings أو raw container configuration.

Testing host تحمل production registration مع narrow test adapters حسب T33؛ لا authz/security-off feature flags. Initial package/image acquisition قد يحتاج network، لكن execution لا يحتاج external providers.

لا production migration/deploy أو privileged cross-schema runtime access داخل verification job. Branch name لا يمنح environment access.

## 6. Reports والدمج

TRX/results تكتب إلى ignored `artifacts/test-results/`، وتجمع لكل suite دون overwrite. Upload للنتائج الآمنة فقط بعد redaction validation، لمدة retention أولية 7 days. لا secrets/config dumps/DB dumps/core dumps ضمن artifacts.

نتيجة PR تذكر SDK/Build warnings/errors وper-suite totals وPostgreSQL major وعزل الهدف وlimitations وفق T33. إذا فشل الاختبار نحتفظ بدليل آمن على الفشل؛ artifact upload failure لا تجعل failed tests نجاحًا.

بعد نجاح أول run فعلية وتأكيد اسم status check، نفعل Required Check للـjob على `main` وفق صلاحيات/خطة GitHub المتاحة. لا phantom required check قبل وجودها، ولا تعطيل failure شرط لتسهيل الدمج.

T40 تظل مسؤولة عن review وSquash Merge. CI الخضراء لا تستبدل قبول التاسك والمراجعة، والفحص المحلي لا يوصف بأنه GitHub Actions run فعلية.

إذا GitHub plan تمنع branch protection، نسجل القيد؛ المساعد يطبق نفس شروط الدمج دون الادعاء بأن GitHub تفرضها server-side.

## 7. Acceptance evidence للتنفيذ

S01-T06 لا تغلق قبل run فعلية على PR للنسخة النهائية المنطبقة، مع workflow link وgreen build/tests وnon-zero discovery وisolated PostgreSQL. يجب إثبات failure propagation عمليًا عند تجهيز pipeline؛ تجربة failing sample تتم على Branch مؤقتة وتصلح قبل الدمج، دون إدخال failing test إلى `main`.

إعادة فحص main بعد الدمج تستخدم event المسجلة، ويعالج أي finding كتاسك fix وفق T40. لا claims نجاح قبل وصول النتيجة.

## مراجع التنفيذ

- [GitHub: Building and testing .NET](https://docs.github.com/en/actions/tutorials/build-and-test-code/net).
- [GitHub: Secure use reference](https://docs.github.com/en/actions/reference/security/secure-use).

هذه الاختيارات تحدد baseline صغيرة للمشروع. SAST/SCA automation التفصيلية وdeployment workflows تضاف عند دخولها النطاق، دون تغيير عقود T35/T36.

## S01-T06 implementation

workflow تطبق full-SHA pins الموثقة في `.github/workflows/ci.yml`. `scripts/verify.py` تنفذ مراحل الفحص وتحتفظ بكل raw output محليًا في run directory منفردة؛ أي process failure تبقى gate failure. لكل suite يراجع التقرير actual results/counters، non-zero discovery، وعدم وجود failed/skipped outcomes.

بدل نشر TRX الخام، ينشر JSON projection allowlisted فقط: suite، counters، static method names بعد حذف theory inputs، وoutcomes. يستبعد stdout/error text/paths/attachments؛ malformed أو inconsistent reports تفشل. ستة report-guard risk tests تثبت zero discovery وfailed/skipped cases وcounter inconsistency وعدم تسريب arbitrary diagnostic values. CI لا ترفع raw reports أو logs.
