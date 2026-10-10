# مراجعة جودة الكود — CQ01

```text
Requested by: Project owner
Date: 2026-10-10
Task: CQ01 — Refactor current implementation for readability and query efficiency
Status: In Review
Integrated base: f5a5c5e — S03-T07
```

## النطاق ومعايير القبول

طلب صيانة للكود المنفذ حاليًا، يشمل `src` و`tests` و`tools` وسكربتات التشغيل والتحقق، ويستخدم المهارات المحلية المتفق عليها. ليس سبرنت ميزات جديدة؛ المرجع التنظيمي للتسليم هو T40، ومرجع السلوك هو عقود S02/S03 والقرارات التقنية المعتمدة.

- يسهل تتبع كل Feature من الطلب إلى الدومين والمعاملة والاستجابة.
- تكون قواعد التنسيق وأدواتها في مهارة المشروع وحدها.
- تقل بيانات القراءات حيث لا تحتاج العملية إلى كيان كامل، مع دليل من SQL واختبارات PostgreSQL.
- تظل عقود HTTP وحدود الموديولات وقواعد الأمان والمعاملات محفوظة.
- تنجح بوابة التحقق الكاملة؛ لا تعطل اختبارات أو شروط مطلوبة لتسهيل التسليم.

المراجعة تشمل التسجيل وOTP والدخول والحساب الحالي وrefresh وإدارة الجلسات والخروج، مع إعداد المضيف وقاعدة البيانات والأسرار وأدوات المشروع. ملفات EF المولدة لم تتغير. وثائق الميزات المحلية لصاحب المشروع خارج هذا التسليم.

## التغييرات القابلة للمراجعة

| الجزء | النتيجة |
| --- | --- |
| Vertical slices | العقود بجوار الـFeature المالكة؛ عقود الدخول/refresh وOTP المشتركة داخل `Features/Shared`؛ resend يستخدم خدمة إصدار مشتركة بدل الاعتماد على Handler أخرى. |
| OTP | فصل الحجز داخل المعاملة عن التسليم بعدها وعن التنظيف عند فشل التسليم؛ سجل التنظيف رسالة آمنة دون هدف أو كود أو proof. |
| التسجيل في DI | فصل إعداد CORS وpersistence وOTP وrate limiting في ملفات واضحة؛ احتفظ ترتيب التسجيلات وخدمات S03-T07. |
| الجلسات | `SessionAccessState` يحمل حقائق التفويض فقط؛ `SessionLifetime` يجمع قاعدة انتهاء الجلسة المستخدمة في الدومين والقراءات. |
| المعاملات | مشاركة تصنيف Busy/Timeout وRollback المؤكدة؛ بقي فشل Commit الذي بدأ يخرج دون تحويله إلى نجاح أو فشل قابل لإعادة المحاولة تلقائيًا. |
| أدوات التشغيل | فصل trust الخاص بـInfisical عن قاعدة البيانات ومشاركة الحفظ الذري للـlocator؛ بقيت شروط الثقة وحماية بيانات التشغيل. |
| الاختبارات | نقل HTTP helpers المشتركة إلى support مستقل، وإغلاق موارد RSA وstreams/provider المستخدمة في الاختبارات. |
| سهولة البدء | [خريطة الكود](../technical/Code%20Map.md) تحدد الملفات المالكة ومسارات الطلبات وأماكن الاختبارات. |

مرجع التنسيق الوحيد هو [educenteros-formatting](../../.agents/skills/educenteros-formatting/SKILL.md). ملفات AGENTS/T42 والسبرنت تشير إليها؛ الأداة الحالية تبني المصادر الموجودة داخل المهارة. بقيت أوامر CI المعتادة صالحة دون نسخة أخرى من القواعد في المشروع.

## دليل تحسين القراءات

قورنت SQL المولدة باستخدام EF Core `ToQueryString` قبل التغيير وبعده، باستخدام مدخلات تشخيصية اصطناعية. اختبارات `QueryProjectionTests` تستخدم PostgreSQL الفعلية، وترصد عدد الأوامر وأسماء أعمدة النتائج دون تسجيل القيم أو معاملات الاتصال.

| القراءة | قبل | بعد | التحقق |
| --- | --- | --- | --- |
| هدف challenge قبل القفل | كيان OTP كامل: 12 عمودًا | `normalized_target`: عمود واحد | أمر واحد، النتيجة الصحيحة/المفقودة، دون tracking. |
| إعادة فحص نشاط challenge بعد التسليم | كيان OTP كامل | قيمة boolean من `EXISTS` | أمر واحد وعمود واحد؛ invalidated/missing يعيدان false. |
| حساب وجلسة middleware | كيانان كاملان: 30 عمودًا | 12 حقل تفويض | أمر JOIN واحد، دون password/hash/contact details أو tracking؛ التفويض والانتهاء الدقيق لهما اختبارات. |
| صفحة تاريخ الجلسات | كيان جلسة: 13 عمودًا | 8 حقول الاستجابة | أمر الصفحة واحد، عزل المالك والترتيب وIsCurrent؛ offset يصل إلى 64-bit دون overflow. |

قراءة قائمة الجلسات تظل Count ثم Page وفق العقد الحالي. شرط المالك وLIMIT/OFFSET parameterized، والترتيب محدد أيضًا على الاستعلام الخارجي بعد تركيب projection. lookup الخاص بـrefresh/logout يشارك projection صغيرة لتحديد الصفوف، ثم يعيد التحقق تحت الأقفال كما كان.

راجعت مفاتيح PK وunique للهاتف والبريد وrefresh hash والفهرس المركب للجلسات `(UserAccountId, CreatedAtUtc, Id)` والفهرس الجزئي للـOTP الحي. لم تضف migration أو index تخمينية. عمليات تغيير الحالة ما زالت تقرأ كيانات tracked تحت الأقفال المناسبة؛ لم تحول قراءاتها إلى snapshots.

هذه أدلة تقليل الأعمدة والـmaterialization، وليست قياسًا لزمن الاستجابة أو throughput. لم تنفذ load test أو `EXPLAIN ANALYZE` على بيانات إنتاج؛ pagination بالصفحات الكبيرة تظل محدودة بطبيعة OFFSET المعتمدة في العقد. اختيار الأعمدة يتبع [إرشادات EF Core الرسمية](https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying)، وقراءات snapshots تتبع [قواعد tracking](https://learn.microsoft.com/en-us/ef/core/querying/tracking).

## مراجعة الاختبارات

الاختبارات الحالية لها نتائج متوقعة ملموسة، وتشمل فشل الأمان والحدود الزمنية الدقيقة والمعاملات والسباقات، بدل الاقتصار على مسارات النجاح. الجرد: 193 method اختبار في 34 ملفًا، بينها 23 اختبار Python؛ الـTheory تنتج حالات تشغيل إضافية. المراجعة استخدمت قواعد المهارة المدمجة لـxUnit/Python؛ لا توجد حزمة language extensions إضافية محلية.

| Finding | Severity | الموضع والأثر | المعالجة |
| --- | --- | --- | --- |
| TA-01 | Low | حقول RSA static في `RuntimeSnapshotTests` و`AuthenticationTokenTests` و`TestingApiFactory` تحتفظ بـ4 handles غير مغلقة طوال العملية. | الاحتفاظ بـPEM اصطناعية فقط؛ إنشاء/import/sign داخل `using`؛ نفس عقود المفاتيح والتوقيع. |
| TA-02 | Low | `RuntimeSecurityTests.UnregisteredOrMismatchedErrorCodes_FailClosedToSanitized500` ينشئ stream/provider/reader دون lifetime صريحة. | إضافة `using` مع بقاء فحوص الرد الآمن وعدم تسريب المدخلات. |

النتيجة: 0 Critical، 0 High، 0 Medium، و2 Low عولجتا. TA-01 finding مشتركة عبر ثلاث فئات، وليست أربع نتائج منفصلة بعدد handles. لم تظهر حاجة لنسخ assertions أو تحويل الاختبارات المتماسكة للمعاملات إلى حالات صغيرة تفقد سياقها.

راجعت assertions وawait والاستثناءات والعزل والوقت والتزامن والتسمية والتكرار والموارد. اختبارات JWT التي تلتقط `SecurityTokenException` تنتهي بـassert رفض صريح. مقارنة identity المختلفة وround-trip وhashing تستهدف عقودًا فعلية؛ ليست مقارنة قيمة بنفسها. اختبارات السباقات تنتظر بوابات ومشاهدة أقفال حقيقية مع deadline، وتحرر الموارد في finally. تعطيل parallelization مرتبط بالـfixtures المشتركة والـenvironment، ولم تغيره المراجعة.

الاختبارات الجديدة تثبت projections وغياب tracking والملكية والترتيب وoffset كبير، وقاعدة lifetime، ومنع اعتماد signatures/fields في Feature على Feature أخرى ومنع اعتماد الدومين على infrastructure/HTTP. حارس العمارة لا يفحص كل تعليمات IL داخل method bodies؛ المراجعة المباشرة تكمله. تحسينات التغطية المستقبلية تشمل قياس load وخطط التنفيذ على أحجام واقعية؛ لا توصف بأنها أخطاء في assertions الحالية.

## نتائج التحقق

محاولة البوابة النهائية `python3 scripts/verify.py` على الحالة المدمجة مع S03-T07:

| الفحص | النتيجة |
| --- | --- |
| hygiene وreport guard وrestore locked | Passed |
| build | Passed؛ صفر warnings/errors |
| formatter self-tests وformatting check | Passed |
| Unit | 176/176 passed؛ صفر failed/skipped |
| Integration | 120/162 passed؛ 42 failed؛ صفر skipped |
| Architecture | 7/7 passed؛ صفر failed/skipped |
| Python المستقلة | 23/23 passed |

المحاولة فاشلة ككل. من 42 حالة Integration: أربع فشلت داخل تجهيز fixture/اتصال PostgreSQL بـsocket/timeout، و38 منعها `TestSafety.CloudProjectAlreadyInUse` بعد ذلك. لم تفشل business assertion في هذه الحالات؛ هذا لا يثبت نجاح الحالات التي لم تصل للتنفيذ. فحص read-only وجد backend خاملة تملك advisory lease؛ لم ينه تشغيلًا آخر أو يتجاوز حارس الملكية لاستكمال الفحص.

الدليل الآمن المحلي: `artifacts/test-results/9a496e070c414df0a0eda387c5f0aaea/safe`. Raw logs/TRX ليست جزءًا من Git أو مستندات التسليم. بوابة المرحلة السابقة قبل دمج S03-T07 نجحت بـ176 Unit و139 Integration و7 Architecture، ومنها ثلاث حالات projections؛ اختبار صفحة الجلسات المضاف بعد S03-T07 ينتظر إعادة الفحص النهائي مع باقي الحالات المتأثرة.

أثناء تشخيص SQL اصطناعية حدث build متزامن تنافس على ملف build output؛ build البوابة نجح، وأعيد التشخيص بعد اكتماله ونجح. لم تغير شروط البوابة أو تضف retries تلقائية. الدمج ينتظر نجاح التحقق على النسخة النهائية.

## حدود النتيجة

المراجعة والاختبارات تؤكد السلوك الذي فحصته؛ لا تقدم ضمانًا أن أي مشروع خالٍ من كل خطأ محتمل. لا توجد ترقيات SDK/packages أو تغييرات في عقود الأعمال أو deployment ضمن هذا الطلب. نتائج الاختبارات الآمنة فقط تصلح للمشاركة؛ السجلات الخام والمدخلات المحلية تبقى ignored.

التسليم عبر `codex/project-code-quality` وPR إلى `main`، مع review وCI قبل Squash Merge وفق T40.
