# T10 - IDs & Codes

## القرار

EduCenterOS يفرق بين:

```text
Internal ID
SystemCode
DisplayCode
Security Secret
External / Legacy ID
```

وكل نوع له غرض مختلف.

---

# 1. Internal IDs

الاختيار الافتراضي للـEntities:

```text
C#         → Guid
Generation → UUID Version 7
Database   → PostgreSQL uuid
Source     → Application-generated
```

مثال:

```csharp
var id = Guid.CreateVersion7();
```

يتم إنشاء الـID قبل حفظ الـEntity.

---

# 2. أمثلة

```text
UserAccount.Id
Institution.Id
Branch.Id

StudentProfile.Id
StudyGroup.Id
StudentEnrollment.Id
ClassSession.Id

Charge.Id
Payment.Id
Refund.Id

TeacherContract.Id
TeacherSettlement.Id
```

---

# 3. لماذا UUID v7؟

لأنها توفر:

```text
Application-side generation
+
Very low collision probability
+
Time-ordered characteristics
+
Better index locality than random UUIDs
```

وتسمح للـEntity بامتلاك Identity قبل `SaveChanges`.

---

# 4. Database-generated Numeric IDs

لا نستخدم كـDefault للـBusiness Entities:

```text
SERIAL
BIGSERIAL
IDENTITY
```

أي استثناء يحتاج سببًا حقيقيًا.

---

# 5. توليد الـID يظل صريحًا

لا نخفي توليد UUID تلقائيًا داخل Base Entity لكل النظام.

بدل:

```csharp
public Guid Id { get; set; } = Guid.CreateVersion7();
```

يفضل أن يكون الإنشاء واضحًا داخل Handler أو Factory المناسبة.

---

# 6. Strongly Typed IDs

لن نستخدم Strongly Typed IDs كقاعدة عامة في Core V1.

الافتراضي:

```csharp
Guid
```

ممكن نستخدم Strong Type انتقائيًا لاحقًا لو ظهر Benefit واضح في:

- Type safety.
- Domain semantics.
- منع أخطاء متكررة بين IDs.

لكن لا نضيف عشرات الأنواع بدون قيمة فعلية.

---

# 7. IDs ليست Security

معرفة:

```text
InstitutionId
StudentProfileId
StudyGroupId
PaymentId
```

لا تمنح أي صلاحية.

دائمًا:

```text
Knowing an ID
≠
Authorization
```

أي Lookup باستخدام ID تظل خاضعة لـ:

```text
Authentication
Authorization
Tenant Isolation
Resource Eligibility
Business Rules
```

---

# 8. SystemCode

`SystemCode` كود يولده النظام عندما يكون هناك احتياج تشغيلي لكود أسهل من UUID.

مثال:

```text
GRP-K7M4X9Q2
```

القواعد:

- ليست Primary Key.
- System-generated.
- Stable.
- Immutable.
- لا يعاد استخدامها.
- لا تعتبر Secret.
- لا تضاف لكل Entity تلقائيًا.
- نطاق الـUniqueness تحدده Business Rules الخاصة بالـEntity.

---

# 9. SystemCode لا تستخدم UUID مقصوصة

لا نستخدم مثلًا:

```text
first 8 chars of UUID
```

كـSystemCode تلقائيًا.

الـSystemCode لها Generator منفصل عندما نحتاجها.

ولا نستخدم Global Sequential Counter كـDefault.

---

# 10. DisplayCode

`DisplayCode` كود تشغيلي موجه للمستخدم أو المؤسسة.

مثال:

```text
G-01
PHY-3S-02
```

الفرق:

```text
SystemCode
→ stable technical/business reference

DisplayCode
→ human-facing operational code
```

---

# 11. DisplayCode للمجموعات

بالنسبة لـStudyGroup:

```text
Institution
+
AcademicYear
+
DisplayCode
```

تحدد نطاق الـUniqueness.

مثال:

```text
2026/2027 → G-12
2027/2028 → G-12
```

قد يكون مسموحًا لأن السنة مختلفة.

---

# 12. تعديل DisplayCode

DisplayCode يمكن تعديلها فقط لو الـDomain تسمح.

أي تعديل يمر عبر:

```text
Authorization
Validation
Normalization
Uniqueness Check
Database Constraint
Audit when needed
```

أما `SystemCode` فتبقى Immutable.

---

# 13. Codes ليست Authorization

كل من:

```text
Internal ID
SystemCode
DisplayCode
```

ليست Security Boundary.

معرفة كود Group أو Payment لا تمنح حق الوصول إليها.

---

# 14. Security Tokens ليست IDs

مفاهيم مثل:

```text
Refresh Token
OTP
Password Reset Token
Verification Token
Recovery Code
```

ليست Entity IDs.

تولد باستخدام:

```text
Cryptographically Secure Random Generation
```

وليس:

```csharp
Guid.NewGuid().ToString()
```

كبديل لمولد أسرار.

---

# 15. ID vs Secret

مثال:

```text
UserSession.Id
→ Identifier

RefreshToken
→ Credential
```

ومثال:

```text
OtpChallenge.Id
→ Identifier

OTP Code
→ Security Secret
```

الاثنان لا يستخدمان لنفس الغرض.

---

# 16. تخزين Secrets

لو النظام يحتاج فقط التحقق من Secret لاحقًا ولا يحتاج استرجاع قيمتها الأصلية:

> نفضل تخزين Hash بدل Raw Value.

ينطبق حسب التصميم على:

```text
Refresh Tokens
OTP Codes
Recovery Codes
Password Reset Tokens
Verification Tokens
```

تفاصيل الـHashing والـrotation والمدة تتبع Security Decisions.

---

# 17. IDs بين الـModules

Module يمكنها الاحتفاظ بـID تابع لمفهوم في Module أخرى.

مثال:

```csharp
Guid InstitutionId;
Guid BranchId;
Guid StudentProfileId;
```

لكن نوع الـID نفسه لا يسمح لها بـ:

- تحميل Entity الأجنبية مباشرة.
- كسر Module boundaries.
- استخدام Navigation Property عابرة للـModules.

قواعد العلاقات تتبع T08.

---

# 18. Database Constraints

Application validation لا تستبدل Constraints.

نستخدم حسب الحاجة:

```text
PRIMARY KEY
UNIQUE
COMPOSITE UNIQUE
NOT NULL
FOREIGN KEY when allowed by T08
```

مثال:

```text
StudyGroup.Id
→ Primary Key

StudyGroup.SystemCode
→ Unique according to its domain rule

StudyGroup.DisplayCode
→ Unique by:
Institution + AcademicYear + DisplayCode
```

---

# 19. External / Legacy IDs

عند Import بيانات من نظام خارجي:

```text
Legacy ID
≠
EduCenterOS Primary Key
```

النظام يولد UUID v7 جديدة.

ويمكن الاحتفاظ بالقيمة القديمة كـ:

```text
LegacyExternalId
SourceSystem
```

عند الحاجة للـmatching أو reconciliation.

---

# 20. إعادة الاستخدام

## Internal ID

لا يعاد استخدامها أبدًا.

## SystemCode

لا يعاد استخدامها بعد إصدارها حتى بعد:

```text
Cancelled
Archived
```

## DisplayCode

إعادة استخدامها تعتمد على الـBusiness Scope الخاصة بها.

---

# القواعد النهائية

1. `Guid / UUID v7` هو Default للـEntity IDs.
2. الـIDs تتولد داخل التطبيق.
3. PostgreSQL تستخدم `uuid`.
4. Numeric IDs ليست Default.
5. Strongly Typed IDs ليست Default.
6. الـID ليست Secret.
7. معرفة ID لا تعني Authorization.
8. SystemCode مستقلة عن الـPrimary Key.
9. SystemCode ثابتة ولا يعاد استخدامها.
10. DisplayCode لها Business scope محددة.
11. Codes لا تستخدم كـSecurity boundary.
12. Security Tokens ليست IDs.
13. Secrets تتولد باستخدام CSPRNG.
14. نفضل Hash للSecrets عندما لا نحتاج استرجاع قيمتها الأصلية.
15. Cross-module IDs لا تكسر Module boundaries.
16. Database Constraints تحمي الـuniqueness والintegrity.
17. Legacy IDs لا تصبح Primary Keys.

---

# خارج نطاق T10

يتم تحديده في قرارات أخرى:

```text
Exact SystemCode format
Prefix lengths
Character sets

Authentication token format
JWT claims
Token lifetime

OTP length / expiry
Refresh token rotation
Recovery code implementation

Idempotency keys
Correlation IDs

External provider IDs
```

---

# القرار النهائي المختصر

> EduCenterOS يستخدم **`Guid` مولدة كـUUID v7 داخل التطبيق** كـInternal IDs الافتراضية.

> `SystemCode` مرجع ثابت يولده النظام عند وجود احتياج Business، بينما `DisplayCode` كود تشغيلي موجه للمستخدم ونطاق تميزه تحدده الـDomain.

> الـIDs والأكواد ليست Secrets ولا تمنح Authorization، بينما Tokens وOTP وRecovery Codes هي Credentials مستقلة وتولد باستخدام مصدر عشوائي آمن.

> Legacy IDs القادمة من Imports تحفظ كمراجع خارجية عند الحاجة ولا تصبح Primary Keys داخل EduCenterOS.