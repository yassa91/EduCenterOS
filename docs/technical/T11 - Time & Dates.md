# T11 - Time & Dates

## القرار

EduCenterOS يفرق بوضوح بين أنواع الزمن المختلفة:

```text
Instant
→ DateTimeOffset
→ UTC
→ PostgreSQL timestamptz

Business Date
→ DateOnly
→ PostgreSQL date

Local Clock Time
→ TimeOnly
→ PostgreSQL time

Time Zone
→ IANA Time Zone ID

Current Time
→ IClock.UtcNow
```

---

# 1. Instants

أي قيمة تمثل **لحظة حقيقية على الخط الزمني** تستخدم:

```csharp
DateTimeOffset
```

وتخزن داخليًا كـUTC.

أمثلة:

```text
CreatedAtUtc
PaidAtUtc
StartsAtUtc
ExpiresAtUtc
RevokedAtUtc
```

في PostgreSQL:

```text
timestamp with time zone
```

---

# 2. Business Dates

لو القيمة تمثل تاريخًا فقط بدون ساعة:

```csharp
DateOnly
```

مثل:

```text
BirthDate
AcademicYearStartDate
ContractStartDate
EnrollmentDate
```

ولا نحول Date إلى Midnight UTC لمجرد سهولة التخزين.

---

# 3. Local Clock Time

الأوقات المحلية بدون تاريخ تستخدم:

```csharp
TimeOnly
```

مثل:

```text
GroupStartTime
GroupEndTime
OpeningTime
ClosingTime
```

---

# 4. Institution Time Zone

كل مؤسسة لديها:

```text
TimeZoneId
```

بصيغة IANA.

الـDefault للمؤسسات المصرية في V1:

```text
Africa/Cairo
```

ولا نستخدم:

```text
UTC+2
UTC+3
Server Local Time
```

كبديل عن Time Zone حقيقية.

---

# 5. Recurring Schedules

الجدول المتكرر يظل Local Business Concept.

مثال:

```text
Sunday
18:00
20:00
```

ولا نحوله إلى UTC قبل معرفة التاريخ الفعلي.

عند إنشاء `ClassSession`:

```text
Local Date
+
StartTime / EndTime
+
Institution TimeZoneId
↓
StartsAtUtc / EndsAtUtc
```

وبالتالي:

```text
GroupSchedulePattern
→ Local

ClassSession
→ UTC Instants
```

---

# 6. DST

قواعد Daylight Saving Time لا تكتب يدويًا داخل النظام.

التحويل يعتمد على الـTime Zone database الخاصة بالRuntime.

لو Local Time كانت:

```text
Invalid
or
Ambiguous
```

بسبب DST:

> العملية تفشل بوضوح ولا يتم تصحيح الوقت صامتًا أو اختيار Offset عشوائي.

---

# 7. Current Time

المصدر الوحيد للوقت الحالي داخل Application Code:

```csharp
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
```

الـDomain لا تستدعي مباشرة:

```csharp
DateTime.Now
DateTime.UtcNow
DateTimeOffset.Now
DateTimeOffset.UtcNow
```

الـHandler تقرأ الوقت ثم تمرره صراحة للـDomain.

```csharp
var nowUtc = clock.UtcNow;

session.Cancel(nowUtc, reason);
```

---

# 8. Read Time Once

لو Use Case تحتاج نفس اللحظة في عدة أماكن:

```csharp
var nowUtc = clock.UtcNow;
```

تُقرأ مرة واحدة وتستخدم في باقي العملية.

مثال:

```text
CreatedAtUtc = nowUtc
ExpiresAtUtc = nowUtc + lifetime
```

---

# 9. Duration vs Calendar Period

## Duration

لو القاعدة تعتمد على مرور مدة زمنية فعلية نستخدم:

```csharp
TimeSpan
```

مثل:

```text
OTP lifetime
Token lifetime
Reservation timeout
Temporary lock
Cooldown
```

## Calendar Period

لو القاعدة تعتمد على التقويم:

```text
Subscription month
Academic year
Contract period
Trial period
Business deadline
```

لا نحولها تلقائيًا إلى عدد ثابت من الساعات.

يعني:

```text
1 month ≠ always 30 days
1 calendar day ≠ always 24 hours
```

---

# 10. Trial & Subscription Periods

T11 **لا تحدد مدة الـTrial**.

المدة تأتي من:

```text
Subscription / Commercial Policy
```

مثلًا لو الـPolicy الحالية قالت:

```text
TrialDuration = N calendar days
```

T11 تحدد فقط طريقة حسابها:

```text
Institution local calendar
↓
Calculate end date
↓
Resolve using Institution TimeZoneId
↓
Store resulting deadline as UTC
```

وبالتالي تغيير مدة الـTrial Business-wise لا يحتاج تغيير Time Architecture.

---

# 11. Deadlines

عند إنشاء Record لها Deadline، نحسب ونخزن القيمة النهائية.

مثال:

```text
CreatedAtUtc
ExpiresAtUtc
```

لو Configuration تغيرت بعد ذلك:

> الـRecords القديمة لا تتغير بأثر رجعي إلا لو Business Rule قالت غير ذلك.

قاعدة الـExpiration:

```text
nowUtc >= ExpiresAtUtc
→ Expired
```

---

# 12. Time Ranges

للـTechnical ranges نفضل:

```text
[Start, End)
```

أي:

```text
Start inclusive
End exclusive
```

مثال:

```text
18:00 → 20:00
20:00 → 22:00
```

بدون overlap عند `20:00`.

ولا نستخدم:

```text
23:59:59.999
```

كحل عام لنهاية اليوم.

---

# 13. API Format

الـAPI تستخدم ISO 8601.

## Instant

```text
2026-08-07T18:00:00Z
```

أو Offset واضح عند قبول Input بذلك:

```text
2026-08-07T21:00:00+03:00
```

قيمة مثل:

```text
2026-08-07T18:00:00
```

ترفض لو الـEndpoint تتوقع Instant لأنها لا تحدد Zone أو Offset.

## Date

```text
YYYY-MM-DD
```

## Time

```text
HH:mm:ss
```

الـFrontend مسؤول عن العرض والLocalization.

---

# 14. PostgreSQL Mapping

```text
DateTimeOffset
→ timestamptz

DateOnly
→ date

TimeOnly
→ time

TimeZoneId
→ text / varchar
```

`timestamptz` تمثل Instant ولا تحفظ:

```text
Africa/Cairo
```

لذلك الـTimeZoneId تحفظ منفصلة عندما تكون جزءًا من الـBusiness Model.

---

# 15. Timestamps

لا نفرض:

```text
CreatedAtUtc
UpdatedAtUtc
```

على كل Entity عن طريق Base Class.

نستخدم Semantic timestamps حسب الـDomain:

```text
ApprovedAtUtc
CancelledAtUtc
VerifiedAtUtc
PaidAtUtc
RecordedAtUtc
ConsumedAtUtc
RevokedAtUtc
```

والـtimestamps لا تعتبر Audit Log كاملة.

---

# 16. Testing

اختبارات الزمن لا تعتمد على:

```text
Thread.Sleep
Task.Delay
Current real time
Machine time zone
```

نستخدم:

```text
Fixed dates
Controllable IClock
Explicit TimeZoneId
```

واختبارات Expiration تغطي:

```text
Before boundary
Exactly at boundary
After boundary
```

كما يجب اختبار تحويلات DST والـPostgreSQL temporal mappings.

---

# القواعد النهائية

```text
Instant              → DateTimeOffset / UTC
Business Date        → DateOnly
Local Time           → TimeOnly
Institution TimeZone → IANA TimeZoneId
Current Time         → IClock.UtcNow

Recurring Schedule   → Local
Actual Occurrence    → UTC

Elapsed Duration     → TimeSpan
Calendar Rule        → Calendar arithmetic

Technical Range      → [Start, End)
```

ولا يعتمد Business Time على Time Zone الخاصة بالسيرفر أو الجهاز.

---

# خارج نطاق T11

T11 لا تحدد:

```text
Trial duration
Subscription duration/pricing
OTP lifetime values
Token lifetime values
Reservation timeout values

Scheduling business rules
Cancellation windows
Attendance windows

Authentication configuration
Retention periods
```

الـDomains المالكة تحدد القيم، وT11 تحدد فقط **التمثيل والحساب الزمني الصحيح**.

---

# القرار النهائي المختصر

> EduCenterOS يستخدم `DateTimeOffset + UTC` للحظات الزمنية، و`DateOnly` للتواريخ، و`TimeOnly` للأوقات المحلية، مع IANA Time Zones صريحة لكل مؤسسة.

> الجداول المتكررة تظل Local، بينما الحصص والأحداث الفعلية تتحول إلى UTC عند تحديد التاريخ.

> الوقت الحالي يأتي فقط من `IClock`، والـDomain تستقبل الوقت صراحة بدل قراءة System Time.

> `TimeSpan` تستخدم للـDurations، بينما الفترات التقويمية تحسب Calendar-wise. T11 لا تحدد مدة الـTrial أو أي Business lifetime؛ هي تحدد فقط كيفية حسابها وتخزينها بصورة صحيحة.