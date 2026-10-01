# T37 - Local Runtime & Docker

## القرار وحالته

```text
Status: Approved implementation baseline
Scope: Local development and isolated automated tests in S01
```

تشغل الـAPI مباشرة على .NET SDK محليًا، وتشغل PostgreSQL 18 في Docker. Docker Compose لقاعدة التطوير ذات التخزين الدائم، وTestcontainers for .NET لبيئة PostgreSQL disposable الخاصة بالاختبارات. لا container image للـAPI أو deployment في S01.

## 1. المرجعية

T01/T04 تملك stack/structure، وT07/T08 تملك PostgreSQL/schema/migrations، وT33 تملك test ownership/isolation، وT34 تملك logs/health، وT35/T36 تملك الأمان والـsecret loading. T38 تملك CI وT39 تملك staging/production topology.

## 2. Toolchain والتثبيت

- البداية على `.NET SDK 10.0.203` الموجودة محليًا؛ `global.json` تستخدم `rollForward: latestPatch` و`allowPrerelease: false`. تحديث feature band قرار موثق ويختبر محليًا وفي CI.
- `net10.0` وstable packages محددة في `Directory.Packages.props`؛ لا floating versions.
- Solution بصيغة `.sln` وفق T04/T33؛ لا تحول الـrunner إلى MTP عند scaffolding.
- Docker Engine وCompose v2 مطلوبتان لتشغيل dev DB، وEngine للاختبارات المعتمدة على Docker.
- Python 3.11+ مسموحة لأدوات local bootstrap الصغيرة؛ ليست dependency داخل الـBusiness Modules أو test runner.
- نسخة PostgreSQL patch مستقرة وimage digest واحدة تثبت في execution manifest عند S01-T05 وتستخدم في dev/tests/CI؛ major 18 ثابتة. لا `latest` أو تحديث تلقائي غير مراجع.
- نسخة Testcontainers وInfisical CLI المثبتة تحدد وتوثق قبل الاستخدام في S01-T05، مع فحص syntax/format الفعلي. هذا gate تنفيذية لا ادعاء وجود التولز الآن.

## 3. التشغيل المحلي

الـAPI Development listener تربط بـloopback، مثل `127.0.0.1`؛ port افتراضية غير سرية موثقة في README، وتعارض port يفشل بوضوح. S01 لا تشغل staging/production؛ لا assume host-only local configuration مناسبة للنشر.

startup environment صريحة canonical من T36؛ missing/conflicting/unknown environment تفشل قبل secret fetch. أدوات dev ترفض اختيار بيئة غير Development، والاختبارات Testing فقط.

`launchSettings.json` إن وجدت تحمل non-secret settings؛ لا auto User Secrets source أو arbitrary inherited environment override للcritical namespaces.

## 4. PostgreSQL Development

- Compose project مخصص لـEduCenterOS، service PostgreSQL واحدة، منفصلة عن خدمات الجهاز الأخرى.
- binding للـhost على loopback وبـport موثقة؛ لا database exposure على جميع interfaces.
- named volume على `/var/lib/postgresql` لصورة PostgreSQL 18؛ `PGDATA` الخاص بها `/var/lib/postgresql/18/docker`.
- قاعدة development باسم صريح؛ credentials خاصة بـdev تأتي من Infisical وقت bootstrap، ولا default reusable passwords أو `trust` host authentication.
- server-admin provisioning credentials منفصلة عن app health/runtime login. API لا تستخدم superuser؛ grants للموديولات تضيفها owning Feature عند الحاجة وفق T08/T36.
- secret password للـcontainer عبر temporary protected file مع `POSTGRES_PASSWORD_FILE`/read-only secret mount، لا secret command arguments أو Compose file ثابتة.
- launcher تحمي permissions وتنظف الملفات عند الانتهاء وتتعامل مع interruption؛ ملفات الأسرار خارج Git. Compose config output التي يمكن أن تكشف secrets لا تطبع.
- restart/recreate يحافظ على volume. لا `down --volumes` كجزء من normal stop أو automated verification.
- تغيير secret في Infisical لا يغير password لcluster موجودة تلقائيًا؛ rotation فيها تحديث DB role ثم validated consumers وفق T36، لا حذف volume كاختصار.

لا schemas أو tables أو migrations لموديولات غير نشطة. الاتصال/readiness هنا infrastructure، لا global application DbContext أو Repository.

## 5. Infisical local bootstrap

المسار الطبيعي هو developer identity الفردية ثم bounded CLI export إلى captured JSON في الذاكرة، ثم تحقق وتكوين startup snapshot وتشغيل child process ببيئة محدودة. Infisical credentials تبقى لدى bootstrap ولا ترثها الـAPI.

العقد:

1. endpoint HTTPS موثوقة وproject ID و`dev` وpath set محددة صراحة قبل authentication/fetch؛ trusted endpoint allowlist محلية مستقلة عن repo locator، ولا default endpoint.
2. `.infisical.json` إن وجدت locator non-secret فقط؛ لا تمنح الثقة أو صلاحية الوصول بذاتها.
3. path الحالية للshared foundation هي `/backend-api/shared`؛ paths إضافية تظهر مع الموديولات النشطة، لا broad recursive fetch.
4. CLI output تلتقط ولا تعرض على terminal أو log أو exception؛ output format/flags تثبت على النسخة pinned قبل تشغيل secrets حقيقية.
5. ترفض duplicate canonical keys وcase collisions وunknown keys والsnapshot الناقصة؛ لا last-folder-wins أو `eval`/shell sourcing.
6. dev DB/runtime key names والnon-secret reviewed manifest تثبت مع implementation في S01-T05. API تستقبل only required settings، لا كل environment الموروثة.
7. reserved critical namespaces تعزل عن command-line/User Secrets/inherited environment طبقًا T36؛ provenance واضحة من launcher snapshot، لا افتراض أن provider precedence تحذف stale keys.
8. فشل fetch/auth/validation يغلق التشغيل، ولا network fetch لكل request أو silent stale fallback.

`UserSecretsFallback` استثناء opt-in Development-only من T36، يحتاج mode صريحة وcomplete independent source واختبارات عدم الدمج؛ ليست الطريق الافتراضية لإغلاق missing Infisical access. S01 لا تعتبر تجربة synthetic Testing دليل نجاح اتصال real dev secrets.

## 6. Automated tests

- Testcontainers تملك disposable PostgreSQL 18 instance لكل run/worker، مع random synthetic password/database name وrandom loopback port.
- CI/local tests لا تقرأ Infisical أو User Secrets أو dev DB credentials؛ fixture تمرر Testing snapshot synthetic صريحة.
- أسماء DB تنتهي `_tests`، لكن الاسم وحده لا يثبت ownership.
- provisioning manifest تجمع container ID/labels وactual target identity وdatabase name وrun-owned lease/marker؛ guard تتحقق منها قبل أي migration/reset/drop.
- cleanup تستهدف الموارد التي أنشأتها fixture فقط، بعد انتظار work الجارية. لا wildcard cleanup أو `docker prune` أو server-wide DB drop.
- cleanup failure تظهر منفصلة مع original failure إن وجدت؛ missing Docker prerequisite failure واضحة لا skip.
- Unit/Architecture لا تحتاج Docker أو DB؛ IntegrationTests serial وفق T33، والتوازي المقصود داخل السيناريو فقط.
- لا owning Module Migrations في S01؛ freshness/upgrade gates تدخل مع أول Module persistence.

## 7. Verification gates

S01-T03: pinned SDK/restore/build.

S01-T04: explicit environment، safe configuration/correlation/health والتشغيل المحلي.

S01-T05: dev Infisical fetch/delivery الفعلية، PostgreSQL connection/restart/persistence، DB outage/readiness، test ownership/cleanup مع negative targets آمنة.

S01-T06: full required tests محليًا وCI، وعدم dependence على real secrets، وتشغيل من checkout جديدة حسب README.

عدم توفر Infisical account/project/dev access يسجل كاعتمادية لـT05؛ لا ادعاء provider integration لم تختبر. لا production operational readiness ضمن هذا القرار.

## مراجع التنفيذ

- [Microsoft: global.json and SDK selection](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json).
- [Docker: PostgreSQL official image](https://hub.docker.com/_/postgres).
- [Testcontainers for .NET: PostgreSQL module](https://dotnet.testcontainers.org/modules/postgres/).
- [Infisical CLI reference](https://infisical.com/docs/cli/reference).
- [Infisical: CLI JSON export example](https://infisical.com/blog/secure-secrets-management-for-cursor-cloud-agents).

المراجع تشرح الأدوات؛ عزل المصادر والـownership والـlauncher والـgates هنا قراراتنا وتحتاج evidence تنفيذ فعلية.
