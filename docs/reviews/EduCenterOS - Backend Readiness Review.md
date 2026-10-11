# مراجعة تشغيل الباك إند — BR01

- التاريخ: 2026-10-11.
- النطاق: مراجعة وإصلاح الجزء المنفذ في `EduCenterOS`، وتشغيل API وSwagger والتحقق من قاعدة البيانات والاختبارات.
- الفرع المحلي: `fix/backend-readiness`؛ مبني على checkout المراجعة CQ01 الذي يتضمن `origin/main` عند `f5a5c5e`.
- ملفات المميزات المعدلة محليًا بواسطة صاحب المشروع محفوظة وخارج هذه الإصلاحات.

## المشاكل التي ثبتت وأصلحت

| المشكلة | السبب والأثر | الإصلاح والدليل |
| --- | --- | --- |
| Swagger يعرض `Failed to load API definition` و`request is not defined` رغم أن OpenAPI يرجع 200. | `UseRequestInterceptor` يستخدم arrow function بمعامل دون أقواس. محلل `parseFunction` في ملفات Swagger المحلية يستخرج المعاملات من الأقواس؛ ينتج دالة لا تربط `request`، وتفشل حتى عند تحميل تعريف API. | استخدام `function (request)`؛ تحميل العمليات الـ11 من المتصفح؛ تنفيذ Logout بلا cookie من HTTPS Swagger يرجع 204؛ تشغيل محلل JavaScript الفعلي على أربعة أنواع طلبات والتحقق من نطاق هيدر CSRF. خمسة اختبارات جديدة تفحص bootstrap وملفات Swagger وحدود Development/Testing. |
| قراءة `Accept` ترفض JSON بالحروف الكبيرة/المختلطة، وتتجاهل أولوية رفض JSON الصريح ومعاملات نوع المحتوى، وتعامل بعض القيم غير الصالحة كأن الهيدر غائب. | مقارنة حساسة لحالة الأحرف و`Any` على الاختيارات غير الصفرية مع قراءة lenient للهيدر. ثبت رفض `APPLICATION/JSON` بـ406، وقبول `application/json;q=0, */*;q=1` رغم رفض JSON الصريح. | قراءة strict؛ مطابقة JSON UTF-8 باستخدام مكتبة HTTP؛ توحيد charset المقتبس وغير الحساس لحالة الأحرف؛ تطبيق أولوية نوع المحتوى والمعاملات قبل الجودة. 21 حالة Unit؛ 9 منها فشلت قبل الإصلاح، ثم نجحت جميعها. |
| وصف OpenAPI ما زال يصف التسجيل وحده. | التعريف النصي لا يعكس مسارات الدخول وrefresh وإدارة الجلسات المنفذة. | تحديث الوصف بما يطابق العمليات الحالية، مع استمرار توضيح أن التسجيل لا ينشئ جلسة أو صلاحيات مؤسسة. |

قواعد مطابقة أنواع المحتوى وأولوية `Accept` موثقة في [RFC 9110 §8.3.1](https://www.rfc-editor.org/rfc/rfc9110.html#section-8.3.1) و[§12.5.1](https://www.rfc-editor.org/rfc/rfc9110.html#section-12.5.1).

## التحقق من التشغيل

- تشغيل bootstrap المحلي المعتمد؛ locked restore وRelease build وIdentityAccess migrations نجحت؛ اتصال Development بـSupabase جاهز.
- `/health/live` و`/health/ready` و`/openapi/v1.json` و`/swagger/index.html` رجعت 200 على HTTP وHTTPS؛ شهادة HTTPS المحلية تحققت دون تجاوز TLS.
- على تشغيل المشغل النهائي، نجحت أيضًا حالات GET/HEAD الأربع لصفحات الصحة على كل من HTTP وHTTPS، مع `no-store` وغياب body في HEAD؛ OpenAPI يعرض المسارات الـ11 وSwagger يرسل interceptor المصحح.
- Swagger يعرض OpenAPI 3.1 و11 عملية، ويخدم ملفات الواجهة محليًا. لا يحفظ Bearer authorization، ولا يستخدم validator خارجي.
- `Try it out` لطلب Logout بلا اعتماد رجع 204 من المتصفح مع الهيدر `X-EduCenterOS-Auth: 1` وOrigin الصحيح.
- ثمانية فحوص HTTPS سلبية نجحت: رفض الحساب والجلسات دون access token؛ رفض refresh بلا cookie؛ رفض Origin غير المسموح والهيدر الناقص؛ رفض حقول JSON الإضافية والبيانات الناقصة؛ التحقق من `no-store` وتطابق correlation ID.
- فحص JavaScript استخدم `parseFunction` وinterceptor من ملف Swagger المرسل فعلًا، وأثبت بقاء الطلب وتطبيق marker على POST الخاص بالمصادقة فقط.
- عشرة فحوص HTTP فعلية بعد إصلاح `Accept` نجحت: الحروف الكبيرة/المختلطة وwildcards وUTF-8 المقتبس؛ أولوية الرفض الصريح؛ رفض ترميز غير مدعوم؛ رفض الهيدر غير الصالح بـ400.
- فحص حزم الحل المباشرة والمتعدية من NuGet لم يبلغ عن حزم ذات ثغرات معروفة وقت الفحص؛ لا يشمل هذا مراجعة أمنية للإنتاج.
- بناء وتشغيل تطبيق macOS المحلي نجحا: `.local/mac-launcher/EduCenterOS.app`؛ ملف مولد ومهمل في Git. وصل المشغل بالنسخة النهائية إلى «التطبيق جاهز» وفتح Swagger، مع زر إيقاف API.

## نتائج الاختبارات

- البناء بعد الإصلاح: ناجح؛ صفر warnings/errors.
- Unit بعد الإصلاح: 202/202؛ صفر failed/skipped.
- تنسيق المشروع و`git diff --check`: ناجحان.
- البوابة الأولى على binaries الأصلية نجحت: 176 Unit و162 Integration و7 Architecture؛ صفر failed/skipped؛ guards/build/formatting ناجحة. الدليل الآمن: `artifacts/test-results/44a4511e4d5247f2b8dc62b2996ae9c4/safe`.
- بوابة التحقق الكاملة على النسخة النهائية بعد الإصلاحات نجحت (`python3 scripts/verify.py`، exit 0): 202 Unit و162 Integration و7 Architecture؛ صفر failed/skipped؛ Python guards وlocked restore وRelease build وformatter self-tests/check وhygiene ناجحة.
- الدليل الآمن النهائي: `artifacts/test-results/5492e4023dd340989d5f0b1f91988945/safe`. السجلات الخام وTRX محلية ومهملة في Git.
- الحالة النهائية: الجزء المنفذ جاهز للتشغيل المحلي وفق الفحوص أعلاه؛ التطبيق وHTTPS Swagger شغالان من مشغل macOS. لا يوجد عطل معروف متبقٍ ضمن نطاق هذه المراجعة.

## حدود الجاهزية

هذه مراجعة للجزء المنفذ حاليًا: التحقق بالموبايل، التسجيل، الدخول، الحساب الحالي، refresh، تاريخ الجلسات، الإلغاء والخروج. بقية مميزات إدارة السنتر غير المنفذة ليست جاهزة لمجرد نجاح الاختبارات الحالية.

بيئتا Staging وProduction ما زالتا معطلتين عمدًا وفق عقد المشروع. إرسال OTP محلي؛ SMS الفعلي وMFA وتجهيز الإنتاج خارج التنفيذ الحالي. لم تستخدم المراجعة بيانات حقيقية أو تنشر credentials أو raw test logs.

في نهاية المراجعة الأولى كانت الإصلاحات محلية ولم يحدث Push أو Merge أو Production deployment. لاحقًا جُمعت إصلاحات BR01 في فرع قبول `fix/s03-t08-authentication-acceptance` من `main` المحدثة، لاستكمال تسليم T40 وإغلاق S03؛ السجل النهائي في [أدلة قبول السبرنت](../verification/S03.md). لم تدخل بيانات التشغيل أو وثائق المميزات المحلية ضمن التسليم.
