# 15 - استيراد Excel ونقل البيانات

## 1. هدف الملف

الملف ده بيحدد إزاي المؤسسات تنقل بياناتها الحالية إلى EduCenterOS بصورة آمنة ومنظمة.

ويحدد بصورة أساسية:

- استيراد Excel وCSV.
- Templates المعتمدة.
- Mapping الأعمدة.
- Staging Area.
- Validation قبل الإدخال.
- Preview / Dry Run.
- اكتشاف التكرار.
- التعامل مع الطلاب الموجودين بالفعل.
- ربط أولياء الأمور.
- استيراد المجموعات والتسجيلات.
- استيراد الوضع المالي الافتتاحي.
- الفرق بين نقل Current State ونقل Historical Data.
- منع إنشاء Login Accounts تلقائيًا من Excel.
- منع منح Roles حساسة من ملف مرفوع.
- التعامل مع الأخطاء.
- Partial Import.
- Idempotency.
- Traceability.
- Audit.
- حماية الملفات المستوردة.
- تصحيح البيانات بعد الاستيراد.
- Assisted Migration.
- التخلص من ملف المصدر بعد انتهاء الحاجة.

الهدف النهائي هو:

> **مساعدة السنتر ينتقل إلى EduCenterOS بدون إجباره على إدخال آلاف السجلات يدويًا، وفي نفس الوقت بدون تحويل Excel إلى وسيلة لتجاوز Business Rules أو Security Controls الخاصة بالنظام.**

---

# 2. حدود الملف

الملف مسؤول عن:

> **Data Import & Initial Migration إلى EduCenterOS.**

ولا يعيد تعريف قواعد الـDomains الأصلية.

مثلًا:

- إنشاء الطالب ومعناه → `02 / 04`.
- Group structure → `03`.
- Enrollment → `04`.
- Finance → `06`.
- Cash → `07`.
- Teacher Contracts → `08`.
- Permissions → `02`.
- Audit → `11`.
- Security → `14`.

الاستيراد **يطبق القواعد دي** ولا يستبدلها.

---

# 3. أهم قاعدة

Excel ليست Source of Authority تتجاوز النظام.

المسار الصحيح:

```text
Excel File
↓
Secure Upload
↓
Parsing
↓
Staging
↓
Mapping
↓
Validation
↓
Duplicate / Conflict Detection
↓
Preview
↓
User Confirmation
↓
Domain Commands
↓
Commit
↓
Import Report
```

وليس:

```text
Excel
↓
INSERT directly into database
```

---

# 4. الاستيراد ليس Direct Database Write

كل Record مستوردة تمر بنفس الـBusiness Validation الأساسية المطلوبة لو المستخدم أنشأها يدويًا.

مثلًا:

طالب مستورد لا يتجاوز:

- Institution isolation.
- required fields.
- duplicate rules.
- branch validation.
- enrollment rules.

---

# 5. الهدف من الـStaging Area

الـStaging تمثل مساحة مؤقتة بين الملف الخارجي والـDomain الحقيقي.

تسمح لنا بـ:

- قراءة البيانات.
- تنظيفها.
- تحليل الأخطاء.
- اكتشاف التكرار.
- عرض Preview.
- اتخاذ قرارات Mapping.

بدون إنشاء سجلات تشغيلية قبل موافقة المستخدم.

---

# 6. `ImportJob`

كل عملية Import لها سجل مستقل مثل:

```text
ImportJob
```

يحتوي على:

- Institution.
- Import Type.
- Source File.
- Template Version.
- Uploaded By.
- Created At.
- Status.
- Branch Scope.
- Mapping.
- Validation Summary.
- Commit Summary.

---

# 7. حالات Import Job

يمكن أن تكون:

- `Uploaded`
- `Scanning`
- `Parsing`
- `NeedsMapping`
- `Validating`
- `NeedsReview`
- `ReadyToImport`
- `Importing`
- `Completed`
- `CompletedWithErrors`
- `Failed`
- `Cancelled`
- `Expired`

---

# 8. أنواع الملفات في Core V1

الـBaseline:

- `.xlsx`
- `.csv`

يمكن دعم Formats أخرى مستقبلًا حسب الحاجة.

---

# 9. Macro-enabled Workbooks

ملفات Excel التي تحتوي Macros أو Active Content لا يتم تنفيذ محتواها.

Core V1 لا يحتاج تشغيل:

- VBA.
- Macros.
- Embedded scripts.

---

# 10. Formulas

EduCenterOS لا ينفذ Formulas الموجودة داخل الملف ككود موثوق.

لو كانت الخلية تعتمد على Formula:

- يتم التعامل مع القيمة المقروءة وفق Parser آمن.
- أو يتم طلب تحويلها إلى Value حسب Import Type.

ولا نعتمد على Formula لتنفيذ Business Logic.

---

# 11. Import Templates

يوفر EduCenterOS Templates رسمية للاستيراد.

مثل:

```text
Students Template
Groups Template
Enrollments Template
Opening Finance Template
Staff Template
```

---

# 12. Template Version

كل Template لها Version.

مثال:

```text
StudentsImport v1
StudentsImport v2
```

عشان النظام يعرف Schema المتوقع.

---

# 13. Template Documentation

كل Template توضح:

- Column name.
- Description.
- Required/optional.
- Data type.
- Example.
- Allowed values.
- Notes.

---

# 14. العربية والإنجليزية

يمكن للـTemplates عرض Headers عربية أو إنجليزية حسب التصميم.

لكن النظام داخليًا يعتمد Canonical Field Names.

---

# 15. Column Mapping

لو المؤسسة لديها ملفها الخاص:

يمكن Mapping مثل:

```text
اسم الطالب
→ StudentName

موبايل ولي الأمر
→ GuardianPhone

كود الطالب
→ LegacyStudentCode
```

---

# 16. Auto Mapping

يمكن للنظام اقتراح Mapping للأعمدة المعروفة.

لكن المستخدم يراجع الاقتراح قبل Import.

---

# 17. Fuzzy Mapping

تشابه أسماء الأعمدة يمكن استخدامه كاقتراح فقط.

لا يتم اعتماد:

```text
"رصيد"
```

تلقائيًا على أنها:

```text
OutstandingBalance
```

بدون مراجعة.

---

# 18. الأعمدة غير المعروفة

يمكن:

- تجاهلها.
- أو يطلب النظام Mapping لها.

لكن لا يتم حفظ بيانات عشوائية داخل النظام لمجرد وجودها في Excel.

---

# 19. Extra Data

لو الملف يحتوي Column لا يوجد لها مكان في EduCenterOS:

لا ننشئ Custom Field تلقائيًا.

يتم تجاهلها أو اعتبارها Unsupported.

---

# 20. أنواع الاستيراد الأساسية

Core V1 يمكن أن يدعم على الأقل:

1. Students.
2. Guardian contact/relationships.
3. Groups.
4. Current Enrollments.
5. Basic Staff / Teacher records.
6. Opening Student Financial Position.

---

# 21. Current-State Migration

الهدف الأساسي من V1 هو:

> **نقل الوضع الحالي للسنتر إلى EduCenterOS.**

مش إعادة بناء كل تاريخ السنتر منذ إنشائه.

---

# 22. Historical Migration

مثل:

- Attendance تاريخية لسنوات.
- كل Payments القديمة.
- كل Cash Shifts القديمة.
- Teacher Settlements القديمة.

دي أكثر تعقيدًا، ويمكن أن تكون:

> **Assisted / Advanced Migration**

وليست Self-Service Requirement أساسية في V1.

---

# 23. Import الطالب

Student Import تنشئ أساسًا:

```text
InstitutionStudentRecord
```

وما يلزم من Identity/Profile linking حسب قواعد النظام.

---

# 24. Import لا ينشئ Login تلقائيًا

قاعدة أساسية:

```text
Excel Student
≠
Automatic UserAccount
```

وجود الطالب داخل السنتر لا يعني إنشاء Credentials له.

---

# 25. إنشاء الحساب لاحقًا

بعد Import يمكن:

```text
InstitutionStudentRecord
↓
Invite / Link Workflow
↓
Student UserAccount
```

وفق ملف 02.

---

# 26. بيانات الطالب الأساسية

يمكن أن تشمل Template الطالب مثلًا:

- Legacy student code.
- Full name.
- Birth date عند توفرها.
- Grade.
- Branch.
- Student phone عند وجوده.
- Guardian name.
- Guardian phone.
- Notes المحدودة المناسبة.

---

# 27. National ID في Import الأساسي

National ID ليست Requirement للاستيراد العادي.

لو يوجد Import لبيانات هوية حساسة:

يكون له:

- Permission أعلى.
- Security controls.
- Data minimization.
- Rules ملف 14.

---

# 28. External / Legacy ID

يستحسن الاحتفاظ بـ:

```text
LegacyExternalId
```

أو Reference مشابه.

وده يساعد في:

- reconciliation.
- update imports.
- traceability.
- linking records.

---

# 29. Legacy ID ليست Primary Key

الـID القديمة لا تصبح الـPrimary Key الداخلية للنظام.

EduCenterOS يحتفظ بمعرفاته الخاصة.

---

# 30. Duplicate Students داخل الملف

قبل Import يتم اكتشاف:

- نفس Legacy ID.
- records متطابقة.
- احتمالات تكرار أخرى.

---

# 31. Duplicate داخل المؤسسة

النظام يقارن بالطلاب الموجودين بالفعل في نفس Institution.

يمكن أن تكون النتيجة:

- New.
- Exact Match.
- Possible Match.
- Conflict.

---

# 32. Possible Match لا يدمج تلقائيًا

مثلًا:

```text
Ahmed Mohamed
same phone
similar DOB
```

يمكن أن يكون Match مرشحًا.

لكن لا يتم Merge بدون Rule موثوقة.

---

# 33. Cross-Institution Identity

لو النظام اكتشف داخليًا أن PersonIdentity قد تكون موجودة في مؤسسة أخرى:

لا يكشف للمؤسسة:

> الطالب موجود في سنتر X.

Tenant isolation تظل سارية.

---

# 34. Linking Identity

لو توجد Identity platform-level يمكن ربطها فقط عبر Workflow آمنة.

الـImport نفسها لا تكشف الحساب أو المؤسسات الأخرى.

---

# 35. Guardian Import

بيانات Guardian يمكن استيرادها كعلاقة تشغيلية للمؤسسة.

لكن:

```text
Imported Guardian Relationship
≠
Platform-Verified Guardian
```

---

# 36. Guardian Assurance

العلاقة المستوردة تبدأ بمستوى Assurance مناسب لمصدرها.

مثل:

```text
InstitutionProvided
```

أو Concept مكافئ.

ويمكن ترقيتها لاحقًا عبر Verification.

---

# 37. دفع قديم لا يثبت Guardian

لو ملف Excel يقول:

```text
Payer = Mohamed
```

ده لا ينشئ GuardianRelationship تلقائيًا.

---

# 38. Staff Import

يمكن Import بيانات الموظفين والمدرسين الأساسية.

لكن لا يتم Import:

- Passwords.
- MFA secrets.
- active sessions.

---

# 39. Staff Login

Import موظف لا يعطيه Login أو Permission تلقائيًا.

يمكن:

```text
Imported Staff Record
↓
Role Review
↓
Invitation
↓
Membership Activation
```

---

# 40. Roles في Import

ملف Import لا يستطيع إنشاء Role جديدة.

وMapping الـRole يكون إلى Roles المعتمدة في ملف 02.

---

# 41. الأدوار الحساسة

مثل:

- PrimaryOwner.
- Financial approver.
- Platform Admin.

لا تمنح تلقائيًا من Excel Import.

---

# 42. PrimaryOwner لا يتم استيراده

PrimaryOwner تتحدد من Institution Creation / Control Workflows.

ولا نسمح Column مثل:

```text
IsOwner = Yes
```

تنقل السيطرة على المؤسسة.

---

# 43. Groups Import

يمكن Import مجموعات حالية.

يجب تحديد:

- Branch.
- Subject.
- Grade.
- Name.
- Capacity.
- Primary Teacher عند إمكانية Mapping.
- Active status.

---

# 44. Schedule Import

يمكن دعم جدول المجموعة ضمن Template إذا كان Schema واضحًا.

لكن Complex schedules غير القابلة للمطابقة تحتاج مراجعة.

---

# 45. عدم استيراد حصص تاريخية ضمن Group Import

استيراد:

```text
Every Saturday 5 PM
```

يعرف Schedule Pattern الحالي.

ولا يعيد إنشاء Sessions السنة الماضية تلقائيًا.

---

# 46. Session Generation بعد Migration

الحصص المستقبلية تبدأ من Effective Migration Date / configured start.

ولا يتم توليد Historical Sessions لمجرد أن الـSchedule Pattern قديمة.

---

# 47. Subject Mapping

الملف قد يحتوي:

```text
Math
رياضيات
ماث
```

النظام يحتاج Mapping إلى Subject صحيحة.

ولا ينشئ 3 Subjects تلقائيًا بسبب اختلاف الكتابة.

---

# 48. Grade Mapping

نفس القاعدة مع:

- الصفوف.
- المراحل.
- الأنظمة التعليمية.

Mapping تكون واضحة.

---

# 49. Branch Mapping

أي Record تحتاج Branch لازم ترتبط بBranch معروفة.

المستخدم لا يستطيع Import بيانات إلى Branch خارج Scope الخاصة به.

---

# 50. إنشاء Branch من Excel

Core Baseline الأفضل:

> Branches الأساسية تنشأ من Workflow المؤسسة العادية.

مش كSide Effect داخل Student Import.

---

# 51. Enrollment Import

Enrollment Import تربط:

```text
Student
+
Group
+
Effective Enrollment State
```

وفق ملف 04.

---

# 52. التسجيل المستورد

لا يتم تغيير Group capacity أو Business Rules لمجرد أن Excel تحتوي عدد طلاب أكبر.

لو Import تكشف Conflict:

يتم عرضه للمستخدم.

---

# 53. Capacity Conflict

مثال:

```text
Group Capacity = 30
Import Enrollments = 35
```

لا يتم تخطي Capacity في الخلفية.

الـImport تحتاج:

- correction.
- أو Business Workflow معتمدة لو يوجد Exception حقيقية.

---

# 54. Enrollment Effective Date

يجب أن يكون لدينا تاريخ واضح يمثل:

> من إمتى نعتبر الطالب داخل EduCenterOS مسجلًا في المجموعة؟

ده مهم لـ:

- Attendance.
- Billing.
- Reports.

---

# 55. Migration Cutoff Date

أي Migration جدية يفضل أن يكون لها:

```text
MigrationCutoffDate
```

مثل:

> البيانات تمثل حالة السنتر حتى 30 سبتمبر.

---

# 56. قبل وبعد Cutoff

النظام يفرق بين:

```text
Legacy history before cutoff
```

و:

```text
EduCenterOS operations after cutoff
```

وده يقلل Double Counting.

---

# 57. استيراد الوضع المالي

أخطر جزء في Self-Service Import هو Finance.

لا نسمح بعمل:

```text
Student.Balance = imported value
```

---

# 58. Opening Financial Position

بدل تعديل Balance نستخدم مفهوم مثل:

```text
MigrationOpeningPosition
```

أو Financial Events مكافئة داخل Ledger.

---

# 59. Financial Cutoff

كل Opening Position لازم تعرف:

> الرصيد ده صحيح حتى تاريخ كام؟

مثال:

```text
Opening position as of 30 Sep 2026
```

---

# 60. لا نستخدم Signed Balance غامضة

الأفضل Template تحتوي مثلًا:

```text
OpeningDueAmount
OpeningCreditAmount
```

بدل:

```text
Balance = -500
```

لأن معنى الإشارة ممكن يختلف بين الأنظمة القديمة.

---

# 61. Opening Due

لو الطالب عليه 500 قبل الانتقال:

يمكن تسجيل حركة مصدرها:

```text
MigrationOpeningReceivable
```

بحيث يصبح الدين مفهومًا وقابلًا للتتبع.

---

# 62. Opening Credit

لو للطالب 200 لصالحه:

تسجل:

```text
MigrationOpeningCredit
```

بدل Balance سالبة غامضة.

---

# 63. Imported Opening Balance لا تعيد بناء التاريخ

لو Opening Due = 500:

ده لا يعني أننا نعرف:

- كانت Charge أصلها كام.
- الطالب دفع كام قديمًا.
- الخصم القديم كان كام.

هي فقط:

> الوضع المالي المعتمد عند Cutoff.

---

# 64. Historical Payments

Self-Service Core V1 لا يحتاج استيراد كل Payment القديمة واحدة واحدة.

لو المؤسسة تحتاج Historical Payment Ledger كامل:

ده Advanced Migration مستقلة.

---

# 65. Opening Financial Position لا تدخل Cash Shift

قاعدة مهمة:

المبالغ القديمة حصلت قبل بدء EduCenterOS.

لذلك Import رصيد قديم:

```text
does not create CashMovement in 07
```

---

# 66. لا نفتح Cash Drawer قديمة

Migration لا تنشئ:

- Cash shifts تاريخية.
- Safes history.
- Daily closing history.

إلا في Migration Advanced منفصلة.

---

# 67. Opening Finance وTeacher Earnings

الـOpening Student Balance لا ينتج تلقائيًا Teacher Earning.

لأن النظام لا يعرف بالضرورة قواعد الاستحقاق التاريخية.

---

# 68. Future Billing بعد Migration

بعد الـCutoff:

Charges الجديدة تتولد بالقواعد العادية في ملف 06.

---

# 69. منع Double Billing

لو الطالب لديه Opening Due تشمل شهر أكتوبر مثلًا:

لازم Migration Configuration تمنع إنشاء نفس الالتزام مرة ثانية.

---

# 70. Billing Start Point

عند Migration يجب تحديد:

```text
Financial system starts generating new obligations from:
<Date / Period>
```

بحيث يكون الحد واضحًا بين Legacy وEduCenterOS.

---

# 71. Historical Attendance

Core V1 لا يحتاج استيراد كل Attendance القديمة.

يمكن البدء بـ:

> Attendance من أول Session بعد Migration Cutoff.

---

# 72. Attendance Summary

لو المؤسسة تحتاج فقط إحصائيات تاريخية:

يمكن مستقبلًا دعم Aggregate Migration منفصلة.

لكن لا ننشئ Attendance Records وهمية لاستكمال الأرقام.

---

# 73. Historical Teacher Settlements

لا يتم استيرادها في Basic Import.

يمكن بدء:

```text
Teacher Settlement accounting from migration cutoff
```

---

# 74. Opening Teacher Payable

لو المؤسسة عندها مستحق سابق للمدرس قبل النظام، يمكن مستقبلًا تمثيله كOpening Adjustment واضح.

لكن مش كجزء افتراضي من Students Import.

---

# 75. Data Validation Levels

Validation تقسم إلى عدة مستويات:

1. File Validation.
2. Structural Validation.
3. Field Validation.
4. Reference Validation.
5. Business Validation.
6. Duplicate Detection.
7. Security Validation.

---

# 76. File Validation

يتحقق من:

- File type.
- size.
- corruption.
- malware.
- supported workbook structure.

---

# 77. Structural Validation

مثل:

- Sheet موجودة.
- required columns موجودة.
- duplicate headers.
- expected template version.

---

# 78. Field Validation

مثل:

- تاريخ صالح.
- مبلغ صالح.
- phone format.
- enum value.
- required field.

---

# 79. Reference Validation

مثل:

```text
Branch = Main
```

هل Branch موجودة؟

```text
Group = G12
```

هل Group موجودة أو موجودة داخل نفس Import Plan؟

---

# 80. Business Validation

مثل:

- Group active.
- enrollment allowed.
- user scope correct.
- capacity.
- financial state.
- duplicate enrollment.

---

# 81. Validation لا تغير البيانات

مرحلة Validation لا تعمل Commit.

---

# 82. Error Severity

يمكن تصنيف Issue إلى:

- `Error`
- `Warning`
- `NeedsDecision`

---

# 83. Error

تمنع Import الـRecord.

مثل:

- missing student name.
- invalid group.
- impossible amount.
- permission violation.

---

# 84. Warning

المعلومة ممكن تستورد لكن تحتاج انتباه.

مثل:

- phone missing.
- optional DOB missing.
- unusual value.

---

# 85. NeedsDecision

مثل:

> هذا الطالب يشبه طالبًا موجودًا بالفعل، هل هو نفس الشخص؟

---

# 86. Error Report

النظام يعرض:

- Sheet.
- Row.
- Field.
- Error code.
- Explanation.
- Suggested action.

---

# 87. عدم كشف البيانات في Logs

Import Error Log التقنية لا تعرض قيمًا حساسة كاملة.

---

# 88. Preview

قبل Commit المستخدم يشوف Summary مثل:

```text
Rows read: 1,250
New students: 1,100
Existing matches: 90
Possible duplicates: 25
Invalid rows: 35
```

---

# 89. Preview مالية

Financial Import تعرض مثلًا:

```text
Opening receivables: 125,000 EGP
Opening credits: 8,500 EGP
Students affected: 420
```

قبل التأكيد.

---

# 90. Reconciliation Totals

يجب توفير Totals تساعد المستخدم يقارن Excel القديمة بالنظام.

مثال:

```text
Legacy active students = 850
Ready to import = 847
Errors = 3
```

---

# 91. Dry Run

أي Import كبيرة يفضل أن يكون لها:

```text
Dry Run
```

تعرض النتيجة المتوقعة بدون Commit.

---

# 92. Confirmation

المستخدم يوافق صراحة على نتيجة Preview قبل Commit.

---

# 93. Step-up للاستيراد الحساس

Import مثل:

- opening financial positions.
- sensitive identity data.
- high-impact bulk update.

يمكن أن يحتاج Step-up Authentication.

---

# 94. Approval

بعض Imports عالية الحساسية يمكن أن تحتاج Approval وفق ملف 11.

خصوصًا:

- Financial opening adjustments.
- Bulk corrections لبيانات موجودة.

---

# 95. Import Modes

يمكن دعم أوضاع واضحة بدل سلوك غامض.

### `CreateOnly`

ينشئ السجلات الجديدة فقط.

### `UpdateExisting`

يحدث Records معروفة بمفتاح موثوق.

### `CreateOrUpdate`

يستخدم فقط عندما يوجد Matching Key واضح.

---

# 96. Default Mode

الـBaseline الأكثر أمانًا:

> `CreateOnly`

للمستخدم العادي.

---

# 97. تحديث Existing Records

لا يتم Update بناءً على Name similarity.

يحتاج Key موثوق مثل:

- EduCenterOS ID.
- Legacy External ID سبق ربطها.
- Institution student code الفريد وفق السياسة.

---

# 98. Field-level Preview في Update

قبل Bulk Update يمكن عرض:

```text
Old Value → New Value
```

للحقول المهمة.

---

# 99. Blank Values

في Update Import:

Blank Cell لا تعني تلقائيًا:

> امسح القيمة القديمة.

لازم يكون هناك Semantics واضحة.

---

# 100. Clear Value

لو المستخدم يريد إزالة قيمة:

يستخدم Marker أو Mode معتمدة بدل Blank غامضة.

---

# 101. Idempotency

إعادة تنفيذ نفس Import Commit لا تنشئ نفس السجلات مرة ثانية.

---

# 102. Import Row Identity

كل Row يمكن أن تحمل:

- ImportJobId.
- Source row number.
- External key.
- Row fingerprint.

حسب التصميم.

---

# 103. إعادة رفع نفس الملف

النظام يمكن أن ينبه:

> الملف ده يبدو إنه اتستورد قبل كده.

لكن لا يعتمد File Hash وحدها لو المستخدم غيّر Record واحدة.

---

# 104. Re-import للتحديث

لو المستخدم يريد تحديث بيانات سبق Importها:

يبدأ Job جديدة في Update Mode.

---

# 105. ImportJob ليست قابلة للتعديل بعد Commit

Mapping وDecisions الخاصة بالـJob المنفذة تحفظ كتاريخ.

Job جديدة تستخدم لأي تغيير لاحق.

---

# 106. Partial Import

Core V1 يمكن السماح باستيراد Rows الصحيحة فقط في Import Types المناسبة.

مثال:

```text
1,000 rows
980 valid
20 invalid
```

يمكن Commit 980 بعد تأكيد المستخدم.

---

# 107. High-Risk Imports

في Financial أو Interdependent Imports يمكن أن تكون السياسة أكثر صرامة.

مثال:

> لا Commit حتى يتم حل كل Financial conflicts المهمة.

---

# 108. Transaction Boundary

لا نحتاج Transaction واحدة ضخمة لعشرات الآلاف من Rows.

لكن كل Business Unit لازم تكون Atomic.

مثل:

```text
Student + InstitutionStudentRecord
```

أو:

```text
Enrollment + required references
```

---

# 109. Import Ordering

في Migration متعددة المراحل:

```text
Branches / Academic references
↓
Students
↓
Guardians
↓
Groups
↓
Enrollments
↓
Opening Finance
```

حسب Dependencies الفعلية.

---

# 110. Migration Project

لو المؤسسة تنقل بيانات كبيرة، يمكن Concept مثل:

```text
MigrationProject
```

يجمع عدة Import Jobs مرتبطة بنفس Cutoff.

---

# 111. Self-Service Import

كل خطط المؤسسة الأساسية يمكنها استخدام Import الأساسية.

Data portability/onboarding الأساسي لا يكون Feature أمنيًا Paywalled.

---

# 112. Assisted Migration

يمكن EduCenterOS تقديم مستوى مساعدة أعلى في بعض الخطط أو الاتفاقات التجارية.

مثل:

- تنظيف ملف قديم.
- بناء Mapping.
- Advanced historical migration.
- support review.

وده لا يغير Business Rules أو Security.

---

# 113. Assisted Migration لا تعني Direct DB Editing

فريق الدعم يستخدم نفس أو أدوات Migration موثقة.

مش SQL يدوي غير مسجل كطريقة عادية.

---

# 114. Scope

المستخدم الذي يستطيع Import لفرع A فقط:

لا يستطيع باستخدام Excel إدخال Records إلى Branch B.

---

# 115. Import Permissions

يمكن وجود Capabilities مثل:

```text
ImportStudents
ImportAcademicData
ImportEnrollments
ImportStaff
ImportOpeningFinance
```

---

# 116. Sensitive Import Permission

`ImportOpeningFinance` مثلًا تكون أكثر حساسية من `ImportStudents`.

---

# 117. Institution Isolation

أي IDs داخل الملف يتم التحقق إنها تخص نفس Institution.

لا يمكن وضع:

```text
GroupId from another institution
```

وتجاوز الـTenant boundary.

---

# 118. Server-side Scope

حتى لو UI أخفت Branch معينة:

Backend يعيد Validation لكل Row.

---

# 119. Import File Security

بعد الرفع:

```text
Upload
↓
Quarantine
↓
Malware / File Validation
↓
Parse
```

قبل المعالجة الفعلية.

---

# 120. Private Storage

Raw Import Files لا تكون Public.

---

# 121. File Retention

ملف المصدر يحتفظ به فقط للمدة المطلوبة:

- validation.
- troubleshooting.
- migration audit.

ثم يحذف حسب Retention Policy.

---

# 122. لا نستخدم Excel كArchive

EduCenterOS لا يحتفظ بكل ملف Migration للأبد لمجرد الاحتياط.

---

# 123. Sensitive Imports

الملفات التي تحتوي بيانات هوية أو مالية يمكن أن يكون لها Retention أقصر ووصول أكثر تقييدًا.

---

# 124. Download Error File

لو تم إنشاء Error Workbook:

- يكون Private.
- Short-lived.
- Scope-protected.
- يسجل تنزيله عند الحساسية.

---

# 125. Spreadsheet Formula Injection

أي CSV أو Excel يتم توليدها من النظام، مثل Error Reports، يجب التعامل مع القيم التي يمكن أن تتحول إلى Formula بصورة آمنة.

خصوصًا القيم التي تبدأ بـ:

```text
=
+
-
@
```

عندما يكون السياق يسمح بخطر Spreadsheet Formula Injection.

---

# 126. Phone Numbers في Excel

Phone columns يجب أن تعامل كنصوص وليس أرقامًا حسابية.

---

# 127. Leading Zeros

لا نفترض أن Excel حافظت على:

```text
010...
```

بصورة صحيحة.

أي Phone/identifier فقد Leading Zero ويصبح Ambiguous يتم Flag له.

---

# 128. Normalization

يمكن Normalize:

- Arabic/English digits.
- spaces.
- basic phone formatting.
- known date formats.

لكن بدون تغيير المعنى بصورة تخمينية.

---

# 129. Dates

يجب التعامل بحذر مع:

- Excel serial dates.
- `dd/MM/yyyy`.
- locale differences.

أي تاريخ Ambiguous يحتاج Review.

---

# 130. Amounts

المبالغ تتعامل كDecimal Monetary Values.

ويجب منع مشاكل:

- floating-point.
- mixed separators.
- invalid currency symbols.

---

# 131. Currency

Core V1 Finance Migration تستخدم:

> EGP

وفق ملفات 01 و06.

---

# 132. Normalization لا يعني تصحيح Business Data

مثال:

```text
grade = "ثانية"
```

ممكن نطلب Mapping.

مش نخمن هل المقصود:

- ثانية إعدادي.
- ثانية ثانوي.

---

# 133. Conflict Resolution

المستخدم يرى Conflict ويختار من Actions المسموحة.

مثل:

```text
Create New
Use Existing
Skip Row
Fix Data
```

حسب نوع Conflict.

---

# 134. Sensitive Match

لو Possible Identity Match على مستوى المنصة:

لا نعرض البيانات السرية للطرف الآخر.

يتم استخدام Secure Link/Review Workflow.

---

# 135. Duplicate Enrollments

لو نفس الطالب بالفعل مسجل في المجموعة:

لا ينشئ Import Enrollment ثانية.

---

# 136. Existing Finance

لو الطالب بالفعل لديه Financial Activity في EduCenterOS:

Import Opening Balance جديدة تحتاج منع أو Review شديد.

---

# 137. Migration Cutoff Lock

بعد بدء التشغيل المالي الحقيقي، لا يفضل السماح بإعادة كتابة Opening Position بشكل متكرر.

---

# 138. تعديل Opening Position

لو تم اكتشاف خطأ بعد Commit:

لا نغير الرقم الأصلي صامتًا.

يتم استخدام Financial Adjustment / Reversal وفق ملف 06.

---

# 139. لا Generic Undo

بعد Commit لا يوجد زر:

```text
Delete everything imported
```

إذا السجلات دخلت بالفعل في التشغيل.

---

# 140. قبل وجود Dependencies

لو Import أنشأت Records لم تستخدم بعد، يمكن السماح بإلغاء/Archive منظم حسب Domain Rules.

---

# 141. بعد وجود Dependencies

لو طالب مستورد:

- حضر.
- دفع.
- دخل Settlement/report.

لا يتم حذفه كجزء من Undo Import.

---

# 142. Import Traceability

السجلات المستوردة يمكن أن تحتفظ بمرجع:

```text
CreatedByImportJobId
```

أو Metadata مماثلة.

---

# 143. Source Metadata

يمكن الاحتفاظ بـ:

- Import job.
- source system label.
- legacy ID.
- migration cutoff.

بدون الاحتفاظ بRaw sensitive row للأبد.

---

# 144. Audit

يتم تسجيل:

- من رفع الملف.
- نوع الـImport.
- Template.
- Mapping.
- Preview confirmation.
- Import start/end.
- counts.
- high-risk decisions.
- financial totals.
- bulk updates.
- failures.
- cancellations.

---

# 145. لا نضع ملف Excel داخل Audit

Audit تحتفظ بMetadata/Reference مناسب.

Raw File في Storage منفصلة حسب Retention.

---

# 146. Import Report النهائي

بعد التنفيذ يظهر:

```text
Created
Updated
Skipped
Failed
Warnings
Conflicts resolved
```

---

# 147. Financial Reconciliation

بعد Opening Finance Import يظهر:

- total opening receivables.
- total opening credits.
- number of students.
- effective cutoff.
- unresolved conflicts.

---

# 148. Enrollment Reconciliation

يعرض مثلًا:

- Enrollments imported.
- skipped duplicates.
- capacity conflicts.
- inactive groups.
- unresolved mappings.

---

# 149. User Confirmation Record

للاستيرادات المهمة يحتفظ النظام بأن المستخدم أكد الـPreview.

---

# 150. Background Processing

الملفات الكبيرة يمكن معالجتها كJob طويلة نسبيًا.

لكن المستخدم يقدر يرى:

- status.
- progress.
- result.

بدون الحاجة لبقاء Browser request واحدة مفتوحة.

---

# 151. Failure Recovery

لو Worker وقع أثناء Import:

إعادة التشغيل لا تنشئ Records مكررة.

---

# 152. Checkpointing

يمكن تقنيًا حفظ Progress/Checkpoint للملفات الكبيرة.

لكن Domain operations تظل Idempotent.

---

# 153. Timeout لا يعني Failure النهائي

لو واجهة المستخدم انتهت Session أثناء Import كبيرة:

Job نفسها يمكن أن تستمر بصورة آمنة وفق النظام.

---

# 154. Cancellation

يمكن إلغاء Job قبل Commit أو أثناء مرحلة آمنة من المعالجة.

بعد Commit أجزاء منها:

لا يتم حذف ما تم إنشاؤه بدون Domain correction rules.

---

# 155. Notifications

يمكن إخطار المستخدم عند:

- validation completed.
- import needs decisions.
- import completed.
- import failed.

عبر ملف 13.

---

# 156. Reporting

ملف 10 يمكن أن يميز:

- imported records.
- organically created records.

عندما يكون ده مفيدًا للتحليل أو Data Quality.

---

# 157. Data Quality بعد Migration

يمكن إنشاء Dashboard مؤقتة بعد Migration تعرض:

- students missing contacts.
- unmatched guardians.
- missing grade.
- unassigned students.
- groups over capacity.
- opening balances needing review.

---

# 158. Import Success لا يعني Data Quality كاملة

ممكن Record تكون Valid تقنيًا لكن ناقصة اختياريًا.

مثال:

```text
Student imported
Phone missing
```

Import ناجحة لكن Data Quality تحتاج تحسين.

---

# 159. Migration Checklist

قبل الانتقال الفعلي يفضل التأكد من:

1. Branches.
2. Academic year.
3. Subjects/grades.
4. Teachers.
5. Groups.
6. Students.
7. Guardians.
8. Enrollments.
9. Opening finance.
10. Reconciliation.

---

# 160. Cutover

يفضل أن تحدد المؤسسة لحظة انتقال واضحة:

```text
Legacy system / Excel
↓
Migration Cutoff
↓
EduCenterOS becomes operational source
```

---

# 161. Parallel Operation

تشغيل Excel وEduCenterOS كمصدرين متساويين لمدة طويلة يزيد خطر:

- duplicate payments.
- duplicate enrollments.
- inconsistent balances.

لذلك لو تم Parallel Run:

يكون محدودًا وله Source of Truth واضحة.

---

# 162. بعد Cutover

الـBaseline:

> العمليات الجديدة تسجل في EduCenterOS.

Excel يمكن تستخدم Export/backup تشغيلي، لكن لا تظل Master System موازية بلا ضوابط.

---

# 163. Historical Evidence

لو السنتر يحتاج الاحتفاظ بExcel القديمة كمرجع قانوني/تشغيلي:

يمكن الاحتفاظ بها خارج Import Runtime وفق سياسة المؤسسة.

EduCenterOS لا تحتاج تخزينها دائمًا.

---

# 164. Password Import ممنوع

لا يتم Import:

- passwords.
- password hashes من نظام قديم.
- recovery codes.
- MFA secrets.
- session tokens.

---

# 165. Account Migration

لو مستقبلًا هننقل User Accounts من نظام آخر:

المسار يحتاج Identity Migration منفصلة وآمنة.

مش Students Excel Import.

---

# 166. Platform Verification Import ممنوع

لا يتم Import Columns مثل:

```text
IsVerified = True
```

لتمنح:

- Public Listing Verification.
- Identity Verification.
- Payment Verification.

Verification تحصل من Workflow المعتمدة.

---

# 167. Audit Import ممنوع

لا يتم إدخال Audit History قديمة باعتبارها EduCenterOS Audit Events أصلية.

لو نحتاج historical archive، يتم تعريف نوع Migration Archive مستقل.

---

# 168. Approval History

نفس الشيء.

Excel لا تنشئ Approvals مزيفة بتاريخ قديم داخل Framework 11.

---

# 169. Marketplace — Phase 2

Marketplace Migration ليست جزءًا من Core V1.

لو تم بناؤها مستقبلًا تحتاج Import منفصلة لـ:

- courses.
- lessons.
- media.
- entitlements.

وفق ملف 09.

---

# 170. Backend Validation

قبل Commit أي Row:

1. Institution صحيحة.
2. User ما زال مخولًا.
3. Branch Scope صحيحة.
4. references موجودة.
5. Domain Rule صحيحة.
6. duplicate rule مطبقة.
7. Import Job في الحالة الصحيحة.
8. Row لم تنفذ سابقًا.

---

# 171. Permission Revalidation

لو المستخدم فقد Permission أثناء تجهيز الملف:

لا يكفي أنه كان عنده Permission وقت Upload.

يتم التحقق مرة أخرى عند Commit.

---

# 172. Business State Revalidation

لو Group كانت مفتوحة وقت Preview وأغلقت قبل Commit:

Enrollment import لا تنفذ بناءً على Preview القديمة.

---

# 173. Preview ليست Authorization دائم

كل Validation الحساسة تعاد وقت Commit.

---

# 174. Concurrency

من الحالات المهمة:

- Import طالب بينما موظف أنشأه يدويًا.
- Import Enrollment بينما Group تمتلئ.
- Import Finance بينما تم تسجيل Payment.
- Jobين يحدثان نفس الطالب.

يتم التعامل باستخدام:

- transactions.
- uniqueness constraints.
- concurrency tokens.
- idempotency.

---

# 175. Domain Entities المقترحة

الأسماء Conceptual وليست أسماء Tables إلزامية.

## Import

```text
ImportJob
ImportFile
ImportTemplate
ImportTemplateVersion
ImportColumnMapping
```

## Staging

```text
ImportStagingRecord
ImportValidationIssue
ImportMatchCandidate
ImportConflict
ImportDecision
```

## Execution

```text
ImportBatch
ImportCommitResult
ImportRowResult
```

## Migration

```text
MigrationProject
MigrationCutoff
LegacyReference
MigrationOpeningPosition
```

---

# 176. العلاقات مع الملفات الأخرى

```text
02
Identity / Membership
        ↑
        │
15 Import
        │
        ↓
03 Academic Structure
04 Enrollment
06 Finance
```

---

# 177. العلاقة مع 14

ملف 14 يحدد:

- file security.
- sensitive data.
- identity protection.
- retention.
- tenant isolation.

ملف 15 يطبقهم على Migration Workflow.

---

# 178. القيود الأساسية

- Excel لا تكتب مباشرة في Production tables.
- كل Import لها Institution واضحة.
- كل Row تخضع لـServer-side validation.
- Import لا تنشئ Login Accounts تلقائيًا.
- Import لا تمنح PrimaryOwner.
- Import لا تمنح Platform Verification.
- Import لا تستورد Passwords.
- Possible duplicate لا يدمج تلقائيًا.
- Opening Finance لا تعدل Balance يدويًا.
- Opening Finance لا تنشئ Cash Movements تاريخية.
- Import committed لا يحذف عشوائيًا.
- نفس Row/Source لا تنفذ مرتين بسبب Retry.
- Raw files لا تحفظ للأبد.
- sensitive files لا تصبح Public.
- Cross-tenant references مرفوضة.

---

# 179. نطاق Core V1

Core V1 يحتاج أساسًا:

- Excel/CSV upload.
- Students import.
- Groups import.
- Current enrollment import.
- Guardian basic data.
- Staff/teacher basic import.
- Opening financial position.
- Mapping.
- Validation.
- Preview.
- Duplicate detection.
- Error reports.
- Import history.

---

# 180. غير مطلوب في Core V1

لا يحتاج Core V1:

- full historical attendance migration.
- full historical payment ledger migration.
- old cash-shift migration.
- old teacher settlement migration.
- generic ETL platform.
- arbitrary database mapping.
- custom scripting.
- automated identity merge.
- direct SQL migration by customers.
- passwords migration.
- complete migration from every competitor automatically.

---

# 181. مميزات مستقبلية

يمكن إضافة:

- Advanced migration wizard.
- Historical attendance migration.
- Historical finance migration.
- migration connectors.
- Google Sheets integration.
- scheduled imports.
- SFTP imports.
- API-based migration.
- automated data cleansing.
- advanced duplicate resolution.
- reusable mapping profiles.
- migration sandbox.

---

# 182. القاعدة النهائية

التدفق الصحيح:

```text
Source Excel
↓
Upload
↓
Security Scan
↓
Parse
↓
Staging
↓
Map
↓
Validate
↓
Detect duplicates/conflicts
↓
Preview
↓
Confirm
↓
Domain Commands
↓
Commit
↓
Reconcile
↓
Import Report
```

والقواعد الحاكمة هي:

> **Excel وسيلة نقل بيانات، وليست طريقة لتجاوز الـDomain.**

> **كل Record مستوردة تمر بنفس Business Validation الأساسية للإنشاء العادي.**

> **Import لا تكتب مباشرة في قاعدة البيانات التشغيلية.**

> **الطالب يمكن استيراده بدون إنشاء UserAccount.**

> **ولي الأمر المستورد لا يصبح Platform-Verified Guardian تلقائيًا.**

> **Role المستوردة لا تتجاوز Role Catalog أو الصلاحيات المعتمدة.**

> **PrimaryOwner لا يمكن تعيينه من Excel.**

> **Verification States لا يمكن منحها من ملف Import.**

> **Possible duplicate لا يندمج تلقائيًا.**

> **المؤسسة لا تعرف من Import إن الطالب موجود في مؤسسة أخرى.**

> **Current-State Migration أهم من إعادة بناء كل التاريخ القديم في V1.**

> **Historical Attendance وCash Shifts وTeacher Settlements ليست Requirement أساسية للـSelf-Service Migration.**

> **الرصيد المالي القديم يدخل كOpening Financial Position واضحة عند Cutoff، وليس كBalance يتم تعديله يدويًا.**

> **Opening Receivable وOpening Credit مفهومان منفصلان بدل Signed Balance غامضة.**

> **Opening Financial Data القديمة لا تنشئ Cash Movements في خزن EduCenterOS.**

> **الـMigration لازم تحدد Cutoff واضحة لمنع Double Billing وDouble Counting.**

> **Preview وReconciliation جزء أساسي من Migration، مش مجرد UX إضافية.**

> **Re-upload أو Retry لا تنشئ Records مكررة.**

> **Update Import لا تعتمد على تشابه الاسم لتعديل Record موجودة.**

> **بعد Commit لا نستخدم Generic Undo تمسح التاريخ؛ التصحيح يتم بقواعد الـDomain المناسبة.**

> **Raw Excel files مؤقتة ومحمية وليست Archive دائمًا.**

> **المستخدم لا يستطيع Import بيانات خارج Branch Scope الخاصة به.**

> **Basic Import متاحة كجزء أساسي من المنتج، بينما Assisted/Advanced Migration يمكن أن يختلف مستوى الدعم التجاري لها.**

الهدف إن السنتر يقدر يقول:

> «عندي 1200 طالب على Excel، إزاي أنقلهم؟»

ويقدر EduCenterOS يجاوب بدقة:

> «قرأنا 1200، منهم 1150 جاهزين، 20 موجودين بالفعل، 15 محتاجين قرار Matching، و15 فيهم Errors.»

وكمان يقدر يجاوب:

> «الـ300 ألف جنيه دي Opening Debts جت منين؟»

> «الطالب ده اتعمل يدوي ولا من Import؟»

> «الـExcel دي اتنفذت قبل كده؟»

> «ليه Student معينة ما اتعملتش؟»

> «هل Import دي أنشأت حسابات دخول للطلاب؟»

> «هل الرصيد القديم دخل خزنة النهارده بالغلط؟»

وتفضل عملية الانتقال إلى EduCenterOS **قابلة للمراجعة، قابلة للتفسير، ومطابقة للـDomain اللي بنيناه في باقي الملفات.**