# خريطة الكود الحالي

هذا الدليل يصف الكود المنفذ حاليًا: التسجيل، التحقق من الهاتف، الدخول، قراءة الحساب، وتجديد الجلسة. الموديولات المستقبلية الموثقة في قرارات المشروع لم تنفذ بعد.

## ابدأ من هنا

| المطلوب تعديله | مكان البداية |
| --- | --- |
| إعداد الـHost وترتيب الـmiddleware | `src/EduCenterOS.Api/Configuration/` |
| حقول عملية أو استجابتها | `src/EduCenterOS.Modules.IdentityAccess/Features/<Operation>/Contracts.cs` |
| عقد HTTP لعملية | `Features/<Operation>/Endpoint.cs` |
| خطوات تنفيذ عملية | `Features/<Operation>/Handler.cs` |
| قاعدة تخص حالة حساب أو جلسة أو OTP | `Domain/` |
| إصدار OTP وإعادة إرساله | `Features/Shared/PhoneVerification/PhoneVerificationIssuance.cs` |
| استجابة الدخول والتجديد المشتركة | `Features/Shared/Authentication/Contracts.cs` |
| قراءة حقائق الأمان للجلسة | `Infrastructure/Persistence/SessionAccessQueries.cs` |
| التحقق من حقائق الجلسة | `Infrastructure/Security/SessionAccessState.cs` و`Domain/SessionLifetime.cs` |
| حدود المعاملات والأقفال | `Infrastructure/Persistence/RegistrationTransactions.cs` و`AuthenticationTransactions.cs` |
| تحويل الفشل بعد Rollback مؤكدة | `Infrastructure/Persistence/TransactionFailures.cs` |
| نموذج EF والقيود والفهارس | `Infrastructure/Persistence/*Mappings.cs` |
| إعدادات التشغيل والأسرار | `Contracts/*RuntimeSettings.cs` و`scripts/dev.py` |

المسارات المختصرة في الجدول تحت `src/EduCenterOS.Modules.IdentityAccess/`.

## مسار الطلب

`Endpoint` تقرأ عقد HTTP، ثم تستدعي `Handler`. الـHandler تجمع البيانات المطلوبة، وتستدعي قواعد الـDomain داخل حدود المعاملة المناسبة، ثم تعيد `Result`. طبقة HTTP تحول النتيجة إلى الاستجابة المتفق عليها.

في الدخول، التحقق من كلمة المرور يجري قبل قفل صف الحساب. المعاملة تعيد قراءة الحساب تحت القفل قبل إنشاء الجلسة والتوكن. في التجديد، ترتيب الأقفال هو الحساب ثم الجلسة ثم سجل refresh؛ الاستجابة تنتظر نجاح Commit.

في OTP، الحجز وتحديث الميزانية يحفظان أولًا داخل معاملة. التسليم يجري بعدها، ثم يعاد فحص حالة التحدي. قراءة رقم الهاتف الأولية تحدد هدف القفل فقط؛ التحقق الحاسم من التحدي يظل داخل المعاملة.

فحص `/accounts/me` يقرأ حقائق الحساب والجلسة في query واحدة. `SessionLifetime` مشتركة بين الجلسة ونسخة القراءة، فتظل حدود انتهاء الجلسة في مكان واحد. قراءة الحساب لا تمدد نشاط الجلسة.

## تسجيل الخدمات

`ModuleRegistration.cs` يوضح خطوات تركيب الموديول. تفاصيلها في الملفات التالية:

- `AccountAuthenticationRegistration.cs`: JWT وAccountSelf.
- `BrowserCorsRegistration.cs`: سياسة المتصفح.
- `PersistenceRegistration.cs`: DbContext وinterceptors.
- `PhoneVerificationRegistration.cs`: التسجيل وOTP والتسليم.
- `RateLimitingRegistration.cs`: سياسات التقييد.
- `EndpointRegistration.cs`: توصيل الـfeatures بمساراتها.

## أين تتحقق من التغيير؟

- `tests/EduCenterOS.UnitTests/`: قواعد الـDomain، parsing، التشفير، وحقائق الجلسات.
- `tests/EduCenterOS.IntegrationTests/`: HTTP وPostgreSQL الفعلية، القيود، السباقات، الإلغاء، وفشل Commit.
- `tests/EduCenterOS.ArchitectureTests/`: حدود الـHost والموديول، وdependencies في توقيعات وحقول الـfeatures والـDomain.
- `tests/verification/`: أدوات التشغيل، bootstrap، ومنع نشر معلومات حساسة من تقارير الاختبارات.
- `QueryProjectionTests.cs`: query واحدة، أعمدة القراءة اللازمة، وعدم تتبع entities في قراءات الاستعلامات المحسّنة.

الفحص الكامل من جذر المشروع:

```sh
python3 scripts/verify.py
```

قواعد التنسيق وأداته موجودة فقط في [educenteros-formatting](../../.agents/skills/educenteros-formatting/SKILL.md). سياسة الاختبارات في [T33](T33%20-%20Testing%20Stack.md)، وتسليم التغييرات في [T40](T40%20-%20Git%20%26%20GitHub%20Workflow.md).
