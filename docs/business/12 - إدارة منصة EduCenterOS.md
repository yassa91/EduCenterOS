# 12 - إدارة منصة EduCenterOS

## 1. هدف الملف

الملف ده بيحدد **إدارة EduCenterOS نفسها كمنصة**، وليس إدارة السنتر من الداخل.

ويحدد بصورة أساسية:

- أدوار مسؤولي المنصة.
- حدود وصول Platform Admin.
- إدارة المؤسسات على مستوى المنصة.
- إنشاء المؤسسة وعلاقتها بالمراجعة.
- الفرق بين `SelfDeclared` والتحقق من الظهور العام.
- `PublicListingVerification`.
- التحقق من بيانات التواصل.
- منع الانتحال والمؤسسات الوهمية.
- البلاغات والشكاوى.
- مراجعة المؤسسات والحسابات.
- تعليق الحسابات أو المؤسسات.
- التعامل مع المخالفات.
- استرجاع المؤسسة في حالات النزاع.
- نقل السيطرة الأساسية `PrimaryOwner`.
- حالات فقدان المالك لحسابه.
- النزاع على ملكية المؤسسة.
- إدارة تكرار المؤسسات.
- منع إساءة استخدام الـTrial.
- الحالات الاستثنائية للاشتراكات.
- الوصول الاستثنائي لدعم العملاء.
- إدارة Platform Admins.
- الرقابة على مسؤولي المنصة.
- Appeals / Requests for Review.
- العلاقة بين Platform Verification وأي ترخيص حكومي أو تحقق من مزود دفع.

الهدف النهائي هو:

> **تمكين EduCenterOS من إدارة المنصة وحمايتها ومعالجة الحالات الاستثنائية بدون التدخل غير الضروري في تشغيل المؤسسات، وبدون إعطاء مسؤولي المنصة وصولًا مفتوحًا أو غير قابل للتتبع إلى بيانات العملاء.**

---

# 2. حدود الملف

الملف مسؤول عن:

> **Platform-Level Administration & Governance**

ويشمل:

- Institutions platform status.
- Public listing verification.
- Trial abuse controls.
- Platform reports.
- Institution reports.
- Impersonation cases.
- Institution ownership/control disputes.
- Platform suspensions.
- Platform admin permissions.
- Exceptional support access.

أما:

- إدارة المؤسسة اليومية → الملفات **02–08**.
- Subscription plans والأسعار → ملف **01**.
- Approval framework → ملف **11**.
- الإشعارات → ملف **13**.
- Security/Identity verification mechanisms → ملف **14**.
- Marketplace moderation → جزء من **09** ويستخدم Framework هذا الملف مستقبلًا.

---

# 3. إدارة المنصة ≠ إدارة المؤسسة

يجب الفصل بين:

```text
Institution Administration
```

و:

```text
Platform Administration
```

---

## Institution Administration

تتم بواسطة:

- `PrimaryOwner`.
- `AuthorizedRepresentative`.
- Institution Managers.
- Branch Managers.

وتخص تشغيل السنتر نفسه.

---

## Platform Administration

تتم بواسطة موظفي EduCenterOS المخولين.

وتخص:

- سلامة المنصة.
- النزاعات.
- التحقق.
- المخالفات.
- الاشتراكات.
- الحسابات الاستثنائية.
- إساءة الاستخدام.

---

# 4. Platform Admin ليس Owner للمؤسسة

وجود Platform Admin لا يعني أنه:

- عضو داخل المؤسسة.
- مدير للمؤسسة.
- Owner.
- يستطيع العمل داخلها كموظف عادي.

الوصول الإداري الاستثنائي يتم بسياق مختلف ومسجل.

---

# 5. مسؤول المنصة `PlatformAdmin`

`PlatformAdmin` صفة داخلية لا يستطيع المستخدم اختيارها لنفسه.

يتم إنشاؤها وتعيينها بواسطة EduCenterOS وفق إجراءات داخلية معتمدة.

---

# 6. عدم وجود Super Admin مفتوح بلا حدود

لا يفضل وجود حساب يومي واحد عنده:

```text
CanDoEverything = true
```

بدون Scope أو Audit.

حتى الأدوار الأعلى تخضع إلى:

- Permissions.
- MFA.
- Sensitive Access Logging.
- Separation of Duties عند الحاجة.

---

# 7. نموذج أدوار المنصة

يمكن تقسيم المسؤوليات داخليًا إلى أدوار مثل:

### `PlatformSupport`

دعم المستخدمين والحالات التشغيلية المحدودة.

### `InstitutionReviewAdmin`

مراجعة المؤسسات وطلبات التحقق.

### `RiskAndAbuseAdmin`

مراجعة إساءة الاستخدام والتكرار والمخالفات.

### `BillingAdmin`

التعامل مع الاشتراكات وحالات الفوترة الاستثنائية.

### `SecurityAdmin`

الحالات الأمنية عالية الحساسية.

### `PlatformAdministrator`

إدارة بعض إعدادات وأدوار مسؤولي المنصة.

مش لازم تتحول كل واحدة إلى Role فعلية منفصلة في أول Version، لكن **فصل الصلاحيات كمفهوم لازم يكون موجودًا**.

---

# 8. صلاحيات Platform Admin تأتي من دور داخلي

المسؤول لا يحصل على صلاحيات بسبب دخوله إلى:

```text
/admin
```

بل من Membership أو Authorization Context داخلي خاص بالمنصة.

---

# 9. MFA

المصادقة متعددة العوامل إلزامية لكل Platform Admin.

بعض العمليات تحتاج Step-up Authentication إضافية.

---

# 10. إدارة مسؤولي المنصة

إنشاء أو تعديل Platform Admin تعتبر عملية حساسة.

تحتاج:

- مستخدم مخول.
- Role واضح.
- Scope.
- Audit.
- MFA.
- Approval عند الصلاحيات الأعلى حساسية.

---

# 11. تعطيل Platform Admin

عند ترك الموظف العمل أو تغير مسؤوليته:

- تسحب صلاحياته.
- تنتهي جلساته.
- يظل Audit القديم باسمه.
- لا يتم حذف تاريخه.

---

# 12. المؤسسة عند الإنشاء

إنشاء Institution لا يحتاج في Baseline موافقة يدوية مسبقة من EduCenterOS حتى يبدأ السنتر استخدام النظام داخليًا.

المسار:

```text
Valid User
↓
Contact Verification
↓
Institution Information
↓
Declaration
↓
Institution Created
↓
PrimaryOwner Membership
↓
Internal Usage
```

---

# 13. `SelfDeclared`

المؤسسة التي أنشأها المستخدم تبدأ بمعلومات:

```text
SelfDeclared
```

وده يعني:

> البيانات مقدمة بواسطة المستخدم المسؤول عنها.

ولا يعني:

- EduCenterOS تحققت من الملكية القانونية.
- المؤسسة مرخصة حكوميًا.
- المؤسسة معتمدة من وزارة التعليم.
- EduCenterOS تضمن صحة كل ادعاء قدمه المستخدم.

---

# 14. الاستخدام الداخلي لا يحتاج Public Verification

المؤسسة تقدر تستخدم EduCenterOS داخليًا في إدارة:

- الطلاب.
- المجموعات.
- الحضور.
- المالية.

بدون أن تكون ظاهرة في Public Search.

---

# 15. حالات الظهور

من ملف 01:

- `Hidden`
- `DirectLinkOnly`
- `PublicSearch`

الحالة لا تساوي Institution Status التشغيلي.

---

# 16. `Hidden`

المؤسسة غير منشورة للعامة.

لكنها يمكن أن تكون:

- Active.
- Paid.
- Fully operational internally.

---

# 17. `DirectLinkOnly`

المؤسسة يمكن أن يكون لها صفحة عامة يمكن الوصول إليها برابط مباشر.

الشروط المبدئية يمكن أن تشمل:

- اكتمال الحد الأدنى للبيانات العامة.
- Contact Verification.
- عدم وجود Block أو Restriction يمنع النشر.

---

# 18. `PublicSearch`

الظهور في نتائج البحث العامة يحتاج مستوى أعلى من الثقة.

ويخضع لـ:

> `PublicListingVerificationPolicy`

---

# 19. Public Listing Verification

الهدف منها تقليل:

- انتحال أسماء السناتر.
- المؤسسات الوهمية.
- البيانات العامة المضللة.
- صفحات Spam.
- سوء استخدام البحث العام.

---

# 20. Public Listing Verification لا تساوي ترخيص حكومي

قاعدة أساسية:

```text
EduCenterOS Public Listing Verified
≠
Government Licensed
```

ولا:

```text
≠ Ministry Accredited
```

إلا لو تم بناء Verification محددة ومعتمدة لهذا الغرض مستقبلًا.

---

# 21. معنى Badge التحقق

لو المنصة تعرض Badge مثل:

```text
Verified
```

لازم توضح **ما الذي تم التحقق منه**.

الأفضل استخدام لغة محددة مثل:

- Contact Verified.
- Public Listing Verified.
- Identity Verified.
- Payment Profile Verified.

بدل Badge عامة توحي بأكثر مما تم إثباته.

---

# 22. Public Listing Verification Policy

يمكن أن تعتمد على مجموعة من الإشارات مثل:

- Contact Verification.
- اكتمال البيانات العامة.
- Owner/account verification.
- Institution name checks.
- Address consistency.
- Duplicate checks.
- Abuse history.
- Supporting information عند الحاجة.
- Manual review في الحالات المشتبه بها.

---

# 23. المراجعة اليدوية ليست شرطًا لكل مؤسسة

الـPolicy ممكن تسمح بـ:

- Automated approval للحالات منخفضة المخاطر.
- Manual review للحالات غير الواضحة.
- Enhanced verification للحالات الحساسة.

مش لازم كل سنتر ينتظر موظف EduCenterOS يدويًا.

---

# 24. Verification Status

يمكن استخدام حالات مثل:

- `NotRequested`
- `Pending`
- `NeedsInformation`
- `Verified`
- `Rejected`
- `Suspended`
- `Expired`

لو نوع التحقق يحتاج انتهاء أو إعادة مراجعة.

---

# 25. Verification Request

يمكن أن يحتوي على:

- Institution.
- Verification Type.
- Requested By.
- Submitted Data.
- Supporting Evidence.
- Status.
- Reviewer.
- Review Reason.
- Created At.
- Decision At.

---

# 26. طلب معلومات إضافية

المراجع لا يحتاج رفض الطلب مباشرة إذا فيه نقص قابل للاستكمال.

يمكن:

```text
Pending
↓
NeedsInformation
↓
Resubmitted
↓
Review
```

---

# 27. رفض التحقق لا يغلق المؤسسة تلقائيًا

رفض Public Listing Verification:

لا يعني بالضرورة:

- تعليق حسابات المؤسسة.
- منع التشغيل الداخلي.
- إلغاء اشتراكها.

يمكن فقط منع:

```text
PublicSearch
```

إلا لو سبب الرفض نفسه يمثل مخالفة خطيرة.

---

# 28. Verification يمكن سحبها

لو بعد التحقق ظهر:

- انتحال.
- بيانات مضللة.
- مخالفة.
- نزاع جدي.

يمكن Suspend أو Revoke Verification.

مع:

- Reason.
- Audit.
- Notification.
- Review/Appeal عند الحاجة.

---

# 29. Contact Verification

يجب الفصل بين:

```text
Phone Verified
```

و:

```text
Institution Verified
```

التحقق من رقم موبايل يثبت التحكم في الرقم، وليس صحة كل بيانات السنتر.

---

# 30. Address Verification

لو تم دعمها مستقبلًا:

لازم يكون معناها محددًا.

مثل:

> تم التحقق من وجود عنوان أو correspondence.

ولا تعني تلقائيًا:

> ملكية المكان أو الترخيص التعليمي.

---

# 31. Legal Entity Verification

لو تم دعم التحقق من LegalEntity مستقبلًا، يكون Type مستقل.

ولا يخلط مع:

- Public Listing.
- Payment Profile Verification.
- Institution ownership inside platform.

---

# 32. Payment Provider Verification

مزود الدفع قد يطلب تحققًا خاصًا لقبول المؤسسة Merchant أو Recipient.

وده:

```text
PSP Verification
```

منفصل عن Public Listing Verification.

---

# 33. رفض PSP لا يغلق السنتر

لو Payment Provider رفض المؤسسة:

يمكن أن تستمر في استخدام:

- Cash.
- Manual payment recording.
- باقي وظائف EduCenterOS.

لكن Electronic Collection عبر المزود لا تتفعل.

---

# 34. المؤسسة المكررة

يمكن أن يحاول أكثر من مستخدم إنشاء نفس السنتر.

النظام يحتاج Duplicate Detection.

---

# 35. Duplicate Detection Signals

يمكن استخدام إشارات مثل:

- الاسم.
- رقم التواصل.
- العنوان.
- Owner identity.
- Public identifiers.
- Legal information عند وجودها.
- Existing verified listing.

لكن لا يتم الدمج تلقائيًا بسبب تشابه اسم واحد.

---

# 36. Possible Duplicate

لو يوجد احتمال تكرار:

يمكن:

- إظهار المؤسسة الموجودة.
- السماح للمستخدم بطلب الانضمام إليها.
- طلب إثبات إضافي.
- إرسال الحالة للمراجعة.

---

# 37. لا دمج تلقائي للمؤسسات

دمج Institution Records عملية عالية الخطورة.

لا يتم تلقائيًا بناءً على Matching Algorithm فقط.

---

# 38. Institution Merge

لو تقرر مستقبلًا دعم دمج مؤسستين مكررتين:

يحتاج Workflow مستقلة تتعامل مع:

- Branches.
- Students.
- Memberships.
- Financial records.
- Subscriptions.
- Public listing.
- Audit.

ويمكن تأجيله خارج Core V1.

---

# 39. Trial Eligibility

من ملف 01:

الـTrial مخصصة لمؤسسة حقيقية مستقلة مرة واحدة وفق السياسة.

لكن رفض Trial جديدة لا يعني رفض استخدام المنتج المدفوع.

---

# 40. Trial Abuse

من أشكال إساءة الاستخدام:

- إنشاء Institutions وهمية متكررة.
- تغيير رقم الهاتف لإنشاء Trial أخرى.
- استخدام عدة Accounts لنفس المؤسسة.
- أرشفة مؤسسة وإنشاء نسخة جديدة للعودة للتجربة.

---

# 41. لا نستخدم Signal واحدة لمنع Trial

ما ينفعش مثلًا:

```text
Same IP
→ Reject Trial
```

لأن نفس الشبكة ممكن يستخدمها أكثر من سنتر أو أكثر من شخص.

---

# 42. Trial Abuse Signals

يمكن استخدام Multi-Signal Risk Assessment مثل:

- Owner account.
- Verified contact.
- Institution details.
- Previous institutions.
- Device signals.
- Payment information عند وجودها.
- Branch/address similarity.
- Historical activity.
- Confirmed duplicates.

---

# 43. Device Fingerprint ليست إثباتًا منفردًا

تستخدم كإشارة مساعدة فقط.

ولا تعامل كدليل نهائي على هوية المؤسسة.

---

# 44. Trial Review Result

يمكن أن تكون:

- `Eligible`
- `Ineligible`
- `NeedsReview`

---

# 45. Trial Ineligible

إذا المؤسسة غير مؤهلة للتجربة:

يظل بإمكانها الاشتراك Paid إذا لم توجد مشكلة أخرى تمنعها.

---

# 46. Manual Trial Exception

Platform Admin مخول يمكن أن يسمح Trial استثنائية في حالات مشروعة.

مثل:

- خطأ سابق.
- إعادة onboarding بعد مشكلة تقنية.
- Demo رسمي.
- حالة تجارية معتمدة.

الاستثناء:

- له Reason.
- له Owner.
- يتسجل Audit.

---

# 47. لا Reset للتجربة بحذف المؤسسة

إغلاق Institution لا يعيد Trial eligibility تلقائيًا.

---

# 48. Institution Platform Status

لازم نفرق بين Subscription Status وبين Platform Status.

يمكن أن تكون Platform Status مثل:

- `Active`
- `Restricted`
- `Suspended`
- `UnderReview`
- `Closed`

---

# 49. `Active`

المؤسسة تعمل بصورة طبيعية وفق اشتراكها.

---

# 50. `Restricted`

بعض الوظائف مقيدة.

مثل:

- Public publishing.
- Electronic payments.
- Invitations.
- Sensitive exports.

بدون تعليق المؤسسة بالكامل.

---

# 51. `UnderReview`

يوجد Review جارية بسبب:

- Report.
- Dispute.
- Risk signal.
- Verification issue.

لا يعني تلقائيًا توقف التشغيل.

---

# 52. `Suspended`

المؤسسة مقيدة بقوة أو ممنوعة من التشغيل حسب سبب التعليق.

لكن بياناتها لا تحذف.

---

# 53. `Closed`

المؤسسة أنهت استخدامها أو تم إغلاقها وفق Workflow.

لا يعني حذف السجلات التاريخية المطلوبة.

---

# 54. Subscription Status منفصلة

ممكن تكون المؤسسة:

```text
PlatformStatus = Active
Subscription = Expired
```

أو:

```text
PlatformStatus = Suspended
Subscription = Paid
```

المفهومان مختلفان.

---

# 55. أسباب التعليق

يمكن أن تشمل:

- Security compromise.
- Serious abuse.
- Impersonation.
- Fraud risk.
- Repeated policy violations.
- Legal requirement.
- Confirmed ownership dispute يستوجب تجميدًا مؤقتًا.
- Payment abuse على مستوى المنصة.

---

# 56. عدم الدفع ليس بالضرورة Platform Suspension

انتهاء الاشتراك يعالج وفق ملف 01:

- Grace period.
- Restricted/read-only mode.

مش لازم نستخدم نفس `Suspended` الخاصة بالمخالفات.

---

# 57. مستويات التعليق

يمكن أن يكون:

### Feature Restriction

تعطيل وظيفة محددة.

### Publishing Suspension

منع الظهور العام.

### Payment Restriction

تعطيل Electronic Payments.

### Institution Suspension

تعليق المؤسسة كلها.

---

# 58. Principle of Minimum Necessary Restriction

لو المشكلة تخص Public Listing فقط:

الأفضل تعليق Public Listing بدل تعطيل كل تشغيل السنتر، إلا لو المخاطر تستدعي أكثر.

---

# 59. Suspension Record

يتضمن:

- Target.
- Restriction Type.
- Reason.
- Started At.
- End Date عند وجودها.
- Created By.
- Review Required.
- Status.

---

# 60. التعليق المؤقت

يمكن أن يكون:

```text
SuspendedUntil = date
```

لكن انتهاء التاريخ لا يعيد التفعيل دائمًا تلقائيًا إذا الحالة تحتاج Review.

---

# 61. رفع التعليق

يتم بعد:

- معالجة السبب.
- Review.
- Approval عند الحاجة.

ويتم الاحتفاظ بتاريخ التعليق السابق.

---

# 62. الحساب الفردي

Platform Admin يمكن أن يحتاج تعليق UserAccount.

وده مختلف عن تعليق Membership أو Institution.

---

# 63. Account Suspension

يمنع تسجيل الدخول للمنصة بالكامل.

يستخدم في حالات مثل:

- Account compromise.
- Abuse.
- Security risk.
- Serious violations.

---

# 64. Membership Suspension

تظل من مسؤولية المؤسسة غالبًا، وليس Platform Admin.

لكن المنصة يمكن أن تتدخل في حالات استثنائية مثل:

- Security.
- Dispute.
- abuse.

---

# 65. البلاغات `Reports`

يمكن للمستخدمين الإبلاغ عن:

- Institution.
- Public listing.
- User.
- محتوى عام مستقبلًا.
- انتحال.
- Spam.
- إساءة استخدام.

---

# 66. إنشاء البلاغ

البلاغ يمكن أن يحتوي على:

- Reporter.
- Target.
- Category.
- Description.
- Evidence.
- Created At.
- Status.

---

# 67. البلاغ ليس إثبات مخالفة

قاعدة مهمة:

```text
Report Submitted
≠
Violation Confirmed
```

لا نعاقب مؤسسة تلقائيًا لمجرد وجود بلاغ.

---

# 68. حالات البلاغ

يمكن أن تكون:

- `Submitted`
- `UnderReview`
- `NeedsInformation`
- `Resolved`
- `Rejected`
- `Escalated`

---

# 69. Report Categories

مثل:

- Impersonation.
- Incorrect Public Information.
- Fraud.
- Abuse.
- Spam.
- Privacy Concern.
- Other.

---

# 70. إساءة استخدام البلاغات

لو مستخدم يرسل بلاغات كيدية أو Spam بشكل متكرر:

يمكن تطبيق Abuse Controls عليه أيضًا.

---

# 71. Moderation Action

بعد Review يمكن أن ينتج:

- No Action.
- Warning.
- Require Correction.
- Remove Public Listing.
- Restrict Feature.
- Suspend Institution.
- Escalate.

---

# 72. Public data correction

لو المشكلة بيانات عامة خاطئة وقابلة للتصحيح:

يمكن طلب تعديل البيانات بدل تعليق المؤسسة كلها.

---

# 73. Impersonation

من أخطر الحالات.

مثل شخص ينشئ Institution باسم سنتر معروف بدون سلطة حقيقية.

---

# 74. Impersonation Review

يمكن استخدام:

- Account identity.
- Contact information.
- Existing verified institution.
- Supporting evidence.
- Historical records.

ولا نعتمد على الاسم وحده.

---

# 75. أثناء نزاع انتحال

يمكن مؤقتًا:

- إخفاء Public Listing.
- وقف نشر بيانات جديدة.
- منع نقل السيطرة.
- المحافظة على التشغيل الداخلي إذا كان آمنًا.

حسب المخاطر.

---

# 76. نزاع السيطرة على المؤسسة

قد يظهر أكثر من شخص يدعي أنه صاحب الحق في إدارة Institution داخل EduCenterOS.

دي مشكلة:

> Platform Control Dispute

ومش بالضرورة إثبات ملكية قانونية للشركة نفسها.

---

# 77. PrimaryOwner يعني Platform Control

من ملف 01 و02:

`PrimaryOwner` هو صاحب السيطرة الأساسية داخل حساب EduCenterOS.

ولا يعني تلقائيًا:

> المالك القانوني الوحيد للمؤسسة.

---

# 78. نقل السيطرة `Ownership Transfer`

الأدق تقنيًا وتجاريًا التفكير فيها كـ:

> Transfer of Primary Platform Control

حتى لو الواجهة تستخدم "نقل الملكية" بصورة مبسطة.

---

# 79. نقل السيطرة الطبيعي

المسار:

```text
Current PrimaryOwner
↓
Select Target User
↓
Target eligibility checks
↓
Step-up authentication
↓
Confirmation
↓
Approval / cooling controls when required
↓
Transfer effective
↓
Old owner role changes
```

---

# 80. شروط Target Owner

يجب مثلًا:

- عنده UserAccount.
- Contact verified.
- مستوفٍ لشروط الأهلية.
- لا توجد Security Restriction تمنعه.
- يقبل السيطرة.

---

# 81. النقل لا يتم بإضافة Role ثانية فقط

القاعدة الأساسية:

> مؤسسة واحدة لها `PrimaryOwner` فعال واحد في نفس الوقت.

لذلك النقل عملية Atomic قدر الإمكان.

---

# 82. بعد النقل

المالك السابق:

- لا يظل PrimaryOwner.
- يمكن أن يتحول إلى Role أخرى لو العملية نصت على ذلك.
- لا تُحذف عملياته القديمة.

---

# 83. AuthorizedRepresentative لا يتحول Owner تلقائيًا

حتى لو يدير المؤسسة منذ سنوات.

التحويل يحتاج Ownership Transfer Workflow.

---

# 84. نقل السيطرة لا ينقل الملكية القانونية تلقائيًا

EduCenterOS تنقل:

> السيطرة على حساب المؤسسة داخل المنصة.

أما آثار الملكية القانونية خارج المنصة فتخضع للواقع القانوني والعقود الخارجية.

---

# 85. فقدان PrimaryOwner لحسابه

قد يحدث:

- فقد رقم الهاتف.
- وفاة.
- Account compromise.
- عدم إمكانية الوصول.
- مغادرة الشخص للمؤسسة.

في الحالة دي نحتاج Recovery Workflow مختلفة عن النقل الطبيعي.

---

# 86. Owner Recovery

الحالة يمكن أن تبدأ بواسطة مستخدم له علاقة بالمؤسسة.

المسار:

```text
Recovery Request
↓
Evidence Collection
↓
Risk Review
↓
Identity / Institution checks
↓
Decision
↓
Transfer / Restore / Reject
```

---

# 87. Recovery ليست Support Override بسيط

موظف الدعم لا يغير PrimaryOwner لمجرد مكالمة هاتفية.

العملية High Sensitivity.

---

# 88. متطلبات Owner Recovery

يمكن أن تشمل حسب الحالة:

- هوية الشخص.
- إثباتات مرتبطة بالمؤسسة.
- Historical account information.
- Existing AuthorizedRepresentative.
- Existing staff confirmations.
- Legal/supporting evidence عند الضرورة.

لكن التفاصيل الدقيقة تحدد في ملف 14 وسياسة التشغيل.

---

# 89. نزاع قائم

لو طرفان يقدمون مستندات متعارضة:

EduCenterOS لا تحاول إصدار حكم قانوني يتجاوز قدرتها.

يمكن:

- تجميد نقل السيطرة.
- الحفاظ على البيانات.
- تقييد تغييرات حساسة.
- طلب تسوية أو مستندات إضافية.

---

# 90. Emergency Security Transfer

لو الحساب الأساسي مخترق:

يمكن Security Team تعليق الحساب والسيطرة الحساسة مؤقتًا.

لكن نقل السيطرة النهائي يظل Workflow منفصلة.

---

# 91. Institution Closure

المؤسسة يمكن أن تطلب إغلاق وجودها على EduCenterOS.

لكن:

```text
Close Institution
≠
Delete Everything
```

---

# 92. قبل الإغلاق

يجب معالجة أو تحذير عن:

- Subscription.
- Active users.
- Open cash shifts.
- Pending refunds.
- Teacher settlements.
- Exports.
- Retention obligations.

---

# 93. إغلاق المؤسسة

بعد الإغلاق:

- يتوقف التشغيل الجديد.
- Memberships لا تستخدم للعمل اليومي.
- Public listing تختفي.
- البيانات تدخل Retention state.
- السجلات المالية/Audit المطلوبة تظل.

---

# 94. Reactivation

يمكن السماح بإعادة تفعيل مؤسسة مغلقة إذا:

- البيانات لا تزال محتفظًا بها.
- المستخدم مخول.
- لا يوجد مانع أمني.
- Subscription يتم استعادتها أو تجديدها.

---

# 95. حذف بيانات المؤسسة

تخضع لسياسات:

- Privacy.
- Retention.
- Legal obligations.
- Financial records.

التفاصيل في ملف 14.

---

# 96. Platform Support

فريق الدعم يحتاج أدوات لحل مشاكل المستخدمين.

لكن لا نحل المشكلة بإعطائه وصولًا كاملًا لكل شيء.

---

# 97. Support Tools

يمكن أن يرى مثلًا:

- Account status.
- Verification status.
- Institution status.
- Subscription status.
- Error references.
- Non-sensitive operational metadata.

---

# 98. الدعم لا يرى كلمات المرور

ولا:

- OTP.
- Authentication secrets.
- Full card data.
- Recovery codes.

---

# 99. Exceptional Data Access

لو حل المشكلة يحتاج بيانات داخل المؤسسة:

يتم استخدام Sensitive/Exceptional Access من ملف 11.

---

# 100. Support Impersonation

لا يفضل أن يدخل موظف الدعم النظام كأنه المستخدم الحقيقي بدون تمييز.

لو تم دعم:

```text
View As / Support Session
```

مستقبلًا، يجب أن يكون:

- واضحًا.
- Read-only قدر الإمكان.
- محدود المدة.
- Reason required.
- Fully audited.

---

# 101. لا تنفيذ عمليات مالية نيابة عن العميل بصمت

Platform Support لا ينشئ:

- Payment.
- Refund.
- Expense.
- Teacher settlement.

داخل المؤسسة وكأنه موظفها بدون Workflow استثنائية واضحة.

---

# 102. Platform Exceptional Action

في الحالات النادرة يمكن أن تتدخل المنصة.

مثل:

- إصلاح Corrupted state.
- Security response.
- Provider inconsistency.
- Compliance requirement.

كل Intervention:

- لها Reason.
- لها Admin.
- لها Audit.
- لها Scope واضح.

---

# 103. Subscription Administration

Platform Billing Admin يمكن أن يتعامل مع:

- Trial status.
- Subscription state.
- Billing exceptions.
- Approved commercial adjustments.

لكن لا يعدل Student Finance الخاصة بالسنتر.

---

# 104. Subscription Credit

لو EduCenterOS نفسها قررت تعويض Institution عن مشكلة في اشتراك المنصة:

ده:

> Platform Subscription Credit

ومش:

- Student Credit.
- Branch Cash movement.

---

# 105. تمديد الاشتراك الاستثنائي

يمكن Admin مخول تنفيذ:

```text
SubscriptionExtension
```

لسبب واضح.

مثل:

- Platform outage.
- Commercial agreement.
- Support resolution.

---

# 106. التمديد لا يمسح الفاتورة القديمة

تظل Billing History واضحة.

---

# 107. تغيير الخطة بواسطة Admin

لو تم في حالة استثنائية:

يحتاج:

- Reason.
- Effective Date.
- Audit.

ولا يتم استخدامه كطريقة للتحايل على Entitlement Rules.

---

# 108. Entitlements

Limits الناتجة عن خطة المؤسسة تظل معرفة مركزيًا.

Platform Admin لا يغير Limits لمؤسسة واحدة بصورة غير موثقة.

لو يوجد Custom Exception:

تسجل كـEntitlement Override محددة.

---

# 109. Entitlement Override

يمكن أن يحدد:

- Institution.
- Feature/Limit.
- Value.
- Start.
- End.
- Reason.
- Approver.

---

# 110. Override مؤقتة أفضل من تعديل الخطة عالميًا

لو Institution تحتاج استثناء مؤقت، لا نغير Definition خطة Basic لكل العملاء.

---

# 111. Platform Configuration

بعض الإعدادات على مستوى المنصة مثل:

- Supported education systems.
- Subscription entitlements.
- Verification rules.
- Feature flags.

تحتاج إدارة داخلية.

لكن File 12 لا يتحول إلى Configuration Catalog كامل.

---

# 112. Feature Flags

يمكن استخدامها لنشر Features تدريجيًا.

لكن:

```text
FeatureFlag
```

لا تعتبر Business Permission.

---

# 113. مؤسسة تجريبية داخلية

EduCenterOS يمكن أن تحتاج Test/Demo Institutions.

يجب تمييزها عن مؤسسات العملاء الحقيقية.

مثال:

```text
InstitutionType = InternalDemo
```

---

# 114. Demo Data لا تدخل تقارير العملاء

ولا تدخل:

- Trial abuse detection العادي.
- Business metrics الحقيقية.
- Public Search.

---

# 115. Platform Reports

مسؤولو المنصة يمكنهم رؤية مؤشرات عامة مثل:

- Institutions.
- Active subscriptions.
- Trials.
- Verification requests.
- Public listings.
- Reports.
- Suspensions.
- Usage limits.
- Payment integration status.

مع الالتزام بالـPrivacy.

---

# 116. Aggregated First

لإدارة المنصة يفضل استخدام Aggregated Data كلما كانت كافية.

مش لازم Platform Admin يرى أسماء الطلاب حتى يعرف:

> المؤسسة لديها 1200 Active Students.

---

# 117. Cross-Institution Access

دي من أخطر الصلاحيات.

لا تعطى إلا لأدوار محددة.

وتطبق:

- Scope.
- Reason.
- Audit.
- Sensitive Access rules.

---

# 118. Platform Search

Admin Console يمكن أن تبحث عن:

- Institution.
- User.
- Subscription.
- Verification.
- Report.

لكن البحث في بيانات حساسة يجب أن يكون محدودًا.

---

# 119. البحث بالرقم القومي

لا يكون Search مفتوحًا لكل Admin.

إذا دعم أصلًا، يحتاج صلاحية عالية وسبب واضح.

---

# 120. Platform Audit

كل العمليات الإدارية المهمة تسجل.

مثل:

- تعليق Institution.
- رفع التعليق.
- Verification decisions.
- Trial exceptions.
- Ownership transfer intervention.
- Platform role changes.
- Exceptional data access.
- Subscription overrides.

---

# 121. Admin لا يحذف Audit

نفس قواعد ملف 11.

---

# 122. Admin Notes

يمكن كتابة Notes داخلية عن Case.

لكن لا تستخدم كبديل للـStructured Status/Reason.

---

# 123. `PlatformCase`

ممكن استخدام Concept موحد للحالات المعقدة.

مثل:

```text
PlatformCase
```

لـ:

- Ownership dispute.
- Impersonation.
- Abuse review.
- Verification escalation.
- Security investigation.

---

# 124. بيانات PlatformCase

يمكن أن تشمل:

- Case Type.
- Target.
- Created By.
- Assigned Team/Admin.
- Priority.
- Status.
- Evidence.
- Internal Notes.
- Decisions.
- Related actions.

---

# 125. حالات PlatformCase

مثل:

- `Open`
- `UnderReview`
- `WaitingForUser`
- `Escalated`
- `Resolved`
- `Closed`

---

# 126. Case لا تغير الحالة وحدها

وجود Case لا يعني Institution Suspended.

Actions الناتجة عنها تكون Records مستقلة.

---

# 127. User Communication

عند اتخاذ إجراء مؤثر، المستخدم المناسب يحتاج Notification واضحة.

مثل:

- Verification rejected.
- Institution restricted.
- Additional information needed.
- Suspension.
- Ownership transfer completed.

---

# 128. لا نكشف Security Intelligence كاملة

الرسالة للمستخدم لا تحتاج أن تشرح كل Signals المستخدمة في Fraud/Abuse Detection لو ده يسمح بالتحايل.

لكنها تكون واضحة بما يكفي لفهم القرار والخطوة التالية.

---

# 129. Appeals / Review Requests

في بعض القرارات يجب السماح للمستخدم بطلب إعادة مراجعة.

مثل:

- Public verification rejection.
- Institution suspension.
- Impersonation decision.
- Trial abuse false positive.

---

# 130. Appeal لا تعني إلغاء القرار

المسار:

```text
Decision
↓
Review Request
↓
Independent or authorized review
↓
Upheld / Changed
```

---

# 131. Appeal Abuse

لا يسمح بإرسال Appeals غير محدودة لنفس القرار بدون معلومات جديدة.

يمكن تطبيق Cooldown أو New Evidence Requirement حسب السياسة.

---

# 132. Verification Reviewer لا يراجع نفسه

لو Admin اتخذ قرارًا حساسًا، Review/Appeal يمكن أن يذهب لمستخدم آخر عندما السياسة تتطلب استقلالًا.

---

# 133. High-Risk Platform Actions

من أمثلتها:

- Transfer PrimaryOwner by platform intervention.
- Permanent institution suspension.
- Sensitive identity override.
- High-level admin role assignment.
- Security recovery.

يمكن أن تحتاج Maker–Checker من ملف 11.

---

# 134. Provider Integration Issues

لو Payment Provider أرسل حالات متعارضة أو حدث outage:

Platform Admin يمكنه مراجعة الحالة.

لكن لا يغير Result إلى `Succeeded` يدويًا بدون Evidence موثوق.

---

# 135. Provider Result Integrity

قاعدة:

```text
Admin Opinion
≠
Provider Transaction Result
```

يمكن:

- Re-query provider.
- Record correction event.
- Escalate.

لكن لا نعيد كتابة التاريخ بلا Source.

---

# 136. Institution Data Export Assistance

لو مؤسسة طلبت مساعدة في تصدير بياناتها:

Platform Support يمكن أن يساعد ضمن Permission وAudit.

لكن البيانات تظل تخص المؤسسة وسياستها.

---

# 137. Migration Support

Platform Team يمكن أن تساعد في Migration كبيرة.

لكن Import الفعلي يظل يخضع لقواعد ملف 15.

---

# 138. Platform Admin وRelated Parties

موظف EduCenterOS ممكن يكون عنده علاقة شخصية بمؤسسة معينة.

لو ظهر Conflict of Interest داخلي:

يمكن تقييد Review أو Approval له.

ده جزء من Internal Governance ويمكن التوسع فيه مستقبلًا.

---

# 139. بيانات الموظفين الداخليين

Institution لا ترى معلومات داخلية عن Platform Admin إلا القدر المناسب في Support/Decision communications.

---

# 140. Marketplace Phase 2

عند تشغيل `09` يستخدم هذا الملف لإدارة:

- Seller verification.
- Course reports.
- Seller suspension.
- Payout holds.
- Marketplace cases.
- Appeals.

لكن تفاصيل Marketplace الأصلية تظل في 09.

---

# 141. Verification Types Registry

الأفضل وجود أنواع مستقلة بدل حقل واحد:

```text
Verified = true/false
```

يمكن مثلًا:

```text
ContactVerification
IdentityVerification
PublicListingVerification
LegalEntityVerification
PaymentProfileVerification
MarketplaceSellerVerification
```

كل واحدة لها Status وPolicy مستقلة.

---

# 142. لا `IsVerified` عامة

وجود Boolean واحدة مثل:

```text
Institution.IsVerified
```

غالبًا هتسبب غموضًا.

لأن:

> Verified إيه بالضبط؟

---

# 143. Domain Entities المقترحة

الأسماء Conceptual.

## Platform Administration

```text
PlatformAdminProfile
PlatformAdminRole
PlatformAdminPermission
```

## Verification

```text
VerificationRequest
VerificationDecision
VerificationEvidence
PublicListingVerification
```

## Institution Control

```text
InstitutionPlatformStatus
InstitutionRestriction
InstitutionSuspension
InstitutionControlTransfer
InstitutionRecoveryRequest
```

## Risk / Abuse

```text
TrialEligibilityAssessment
TrialException
DuplicateInstitutionMatch
AbuseReview
```

## Reports / Cases

```text
PlatformReport
PlatformCase
PlatformCaseAction
PlatformAppeal
```

## Subscription Exceptions

```text
SubscriptionExtension
EntitlementOverride
```

---

# 144. العلاقات مع الملفات الأخرى

```text
01
Product / Subscription / Public Visibility
          ↓
12
Platform Administration
```

```text
02
PrimaryOwner / Membership
          ↓
12
Control Transfer / Recovery
```

```text
11
Approval / Audit Framework
          ↓
12
Platform-sensitive actions
```

```text
14
Identity / Security / Verification mechanisms
          ↓
12
Platform review decisions
```

---

# 145. Backend Validation لـPublic Verification

يتحقق من:

1. Institution موجودة.
2. المستخدم مخول بالطلب.
3. البيانات الأساسية مكتملة.
4. Contact verification المطلوبة موجودة.
5. لا توجد Blocking Restriction.
6. لا توجد Request مكررة فعالة.
7. Policy version معروفة.
8. Reviewer مخول عند الحاجة.

---

# 146. Backend Validation للتعليق

يتحقق من:

- Admin Permission.
- Reason.
- Scope.
- Suspension Type.
- Approval عند الحاجة.
- Target status.
- عدم تكرار تعليق متعارض.

---

# 147. Backend Validation لنقل السيطرة

يتحقق من:

1. Institution صحيحة.
2. Current PrimaryOwner معروف.
3. Target user معروف ومؤهل.
4. Verification المطلوبة تمت.
5. Security checks.
6. Approval المطلوبة.
7. العملية غير منفذة سابقًا.
8. يوجد PrimaryOwner واحد فقط بعد الإتمام.

---

# 148. Backend Validation للـTrial

يتحقق من:

- Institution identity signals.
- Previous trials.
- Duplicate signals.
- Existing exception.
- Risk assessment.
- Final eligibility.

ولا يستخدم Client-side decision فقط.

---

# 149. Idempotency

مطلوبة في:

- Institution creation.
- Verification submission.
- Suspension.
- Reactivation.
- Ownership/control transfer.
- Trial exception.
- Subscription override.

---

# 150. Concurrency

من الحالات المهمة:

- شخصان يحاولان نقل PrimaryOwner.
- Verification Decisionين في نفس الوقت.
- Suspend وReactivate معًا.
- Institution duplicate merge review.
- Trial eligibility أثناء إنشاء Institution أخرى.

يحتاج النظام:

- Transactions.
- Concurrency Tokens.
- State Validation.
- Idempotency.

---

# 151. القيود الأساسية

- Institution لها PrimaryOwner فعال واحد.
- Platform Admin ليست Institution Membership.
- Public Listing Verification مستقلة عن Legal/PSP verification.
- Rejected verification لا تحذف المؤسسة.
- Trial ineligibility لا تمنع Paid subscription وحدها.
- Platform suspension لا تحذف البيانات.
- Provider result لا يعدل يدويًا بلا Source.
- Platform Admin sensitive access تسجل.
- Duplicate detection لا يدمج المؤسسات تلقائيًا.
- Audit لا تحذف.
- بيانات المؤسسات معزولة حتى من أدوات Admin بقدر الحاجة.

---

# 152. النطاق غير المدعوم في Core V1

لا يحتاج Core V1:

- Enterprise case-management platform كاملة.
- Automated legal verification.
- Government licensing integration.
- Fully automated fraud engine.
- Automated institution merging.
- Advanced risk scoring باستخدام ML.
- Generic moderation engine لكل أنواع المحتوى.
- Unlimited workflow builder.
- Platform support impersonation unrestricted.
- Platform Admin universal database access.

---

# 153. مميزات مستقبلية

يمكن إضافة:

- Advanced risk scoring.
- Government registry integrations.
- Automated address verification.
- Advanced KYC/KYB providers.
- Fraud anomaly detection.
- Platform case SLA engine.
- Admin delegation.
- Advanced appeal workflows.
- Institution merge workflow.
- Business account recovery automation.
- Compliance integrations.
- Marketplace moderation tooling المتقدم.

---

# 154. القاعدة النهائية

المؤسسة تبدأ:

```text
User Creates Institution
↓
SelfDeclared
↓
Internal Operation Allowed
```

ولو تريد الظهور العام:

```text
Complete Public Profile
↓
Contact Verification
↓
Public Listing Verification Policy
↓
PublicSearch
```

ولو حدثت مشكلة:

```text
Report / Risk Signal
↓
Platform Review
↓
No Action
or
Correction Required
or
Restriction
or
Suspension
```

ونقل السيطرة:

```text
PrimaryOwner
↓
Control Transfer Workflow
↓
Target User Verification
↓
Security / Approval
↓
New PrimaryOwner
```

والقواعد الحاكمة هي:

> **Platform Admin وإدارة المؤسسة حاجتان مختلفتان.**

> **Platform Admin لا يحصل على وصول مفتوح لكل بيانات العملاء لمجرد دوره.**

> **إنشاء مؤسسة واستخدامها داخليًا لا يحتاج Public Listing Verification مسبقة.**

> **SelfDeclared لا تعني أن EduCenterOS تحققت من الترخيص أو الملكية القانونية.**

> **Public Listing Verification هدفها الثقة في الظهور العام وتقليل الانتحال، وليست اعتمادًا حكوميًا.**

> **Contact Verification وIdentity Verification وPublic Listing Verification وPSP Verification أنواع مختلفة ولا يتم اختصارها في IsVerified واحدة.**

> **رفض Public Listing لا يعني إغلاق السنتر أو منعه من استخدام النظام داخليًا.**

> **Trial Abuse يتم تقييمها بعدة Signals، ولا نعتمد على IP أو Device وحده.**

> **عدم أهلية المؤسسة لـTrial لا يمنعها من الاشتراك المدفوع وحده.**

> **Duplicate Detection لا تؤدي إلى دمج تلقائي للمؤسسات.**

> **تعليق Public Listing أفضل من تعليق المؤسسة كلها لو المشكلة تخص النشر فقط.**

> **Subscription Expired وPlatform Suspended حالتان مختلفتان.**

> **PrimaryOwner يمثل السيطرة الأساسية على المؤسسة داخل EduCenterOS، وليس حكمًا نهائيًا من المنصة على الملكية القانونية خارجها.**

> **AuthorizedRepresentative لا يصبح PrimaryOwner تلقائيًا.**

> **نقل السيطرة من أعلى العمليات حساسية ويحتاج Verification وAudit واضحين.**

> **الدعم لا يغير Owner أو بيانات مالية حساسة بمجرد طلب شفهي.**

> **Provider Transaction Result لا يعدل يدويًا لمجرد رأي Admin.**

> **Platform Exceptional Access لها سبب وScope وAudit.**

> **الإجراءات على مستوى المنصة تطبق مبدأ أقل صلاحية ممكنة.**

> **قرارات المنصة المهمة يمكن أن تدعم Review أو Appeal بدون أن يعني ذلك إلغاء القرار تلقائيًا.**

الهدف إن EduCenterOS تقدر تدير آلاف المؤسسات مستقبلًا، وتحل مشاكل حقيقية زي:

> «فيه اتنين عملوا نفس السنتر، مين الحساب الصحيح؟»

> «المؤسسة دي ينفع تظهر في البحث العام ولا لأ؟»

> «السنتر بيعمل Accounts جديدة عشان ياخد Trial كل مرة؟»

> «صاحب السنتر فقد حسابه؛ إزاي نرجع السيطرة بشكل آمن؟»

> «في بلاغ انتحال، نوقف السنتر كله ولا نخفي الصفحة العامة فقط؟»

> «موظف الدعم احتاج يشوف معلومة حساسة؛ مين سمح له وليه؟»

> «Admin مد الاشتراك أسبوعًا؛ مين عمل ده وليه؟»

وتكون كل الإجابات ناتجة من **Policies وVerification Types وPlatform Cases وAudit واضحة**، مش قرارات يدوية غير موثقة.