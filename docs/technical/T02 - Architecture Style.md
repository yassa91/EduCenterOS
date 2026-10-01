# T02 - Architecture Style

## القرار

EduCenterOS سيستخدم:

```text
Modular Monolith
+
Vertical Slice Architecture
+
Pragmatic DDD
```

مع:

```text
Single Backend Application
Single Deployment initially
Single Process initially
Single Database initially
```

ولا نبدأ بـMicroservices.

---

## 1. Modular Monolith

النظام تطبيق واحد مقسم داخليًا إلى **Business Modules واضحة**.

كل Module:

- تملك الـBusiness Logic الخاصة بها.
- تملك بياناتها.
- تملك Use Cases الخاصة بها.
- لا تعدل بيانات Module أخرى مباشرة.
- تتواصل مع باقي الـModules من خلال Contracts أو Events واضحة.

الهدف هو الحصول على حدود قوية بدون تعقيد الأنظمة الموزعة.

---

## 2. Vertical Slice Architecture

كل Use Case تتنظم كـFeature مستقلة داخل الـModule المالكة لها.

مثال:

```text
Academic/
└── Features/
    ├── CreateGroup/
    ├── ChangePrimaryTeacher/
    ├── ChangeGroupCapacity/
    └── GetGroupDetails/
```

الـSlice يمكن أن تحتوي حسب الحاجة على:

```text
Endpoint
Request
Response
Validator
Handler
```

ولا ننظم النظام أساسًا حول مجلدات ضخمة مثل:

```text
Controllers/
Services/
Repositories/
```

تجمع Features غير مرتبطة ببعض.

---

## 3. Pragmatic DDD

نستخدم DDD عندما توجد Business Rules حقيقية ومعقدة.

مثل:

```text
Enrollment & Seat Capacity
Attendance
Student Finance
Cash Operations
Teacher Compensation
Approvals
Institution Control
```

أما CRUD البسيط والبيانات المرجعية فلا نضيف لها Aggregates وFactories وPatterns بدون قيمة فعلية.

---

## 4. حدود الـModules

القاعدة:

> كل Module تملك منطقها وبياناتها، ولا يتم تجاوز حدودها لمجرد أن النظام يعمل داخل Process أو Database واحدة.

التواصل يمكن أن يتم من خلال:

```text
Public Contracts
Application Use Cases
Queries / Commands
Domain Events
Integration Events
Read Models
```

ويتم اختيار الأسلوب المناسب حسب احتياج كل حالة.

---

## 5. Events ليست Microservices

عدم استخدام Message Broker في V1 **لا يعني منع Events**.

مسموح استخدام:

```text
Domain Events
Integration Events
Transactional Outbox
Background Workers
```

داخل الـModular Monolith عندما نحتاج:

- Reliable side effects.
- Notification delivery.
- Reporting projections.
- Decoupling بين Modules.
- تنفيذ عمليات لا تحتاج Consistency فورية.

لكن لا نضيف:

```text
Kafka
RabbitMQ
Distributed Message Broker
```

إلا لو ظهر احتياج تشغيلي حقيقي يبرر ذلك.

---

## 6. الاتساق الفوري مقابل غير الفوري

مش كل التواصل بين Modules يتحول إلى Event.

لو Business Invariant تحتاج نتيجة صحيحة فورًا، نستخدم آلية تحقق/Transaction مناسبة.

أما العمليات التي تقبل Eventual Consistency فيمكن تنفيذها من خلال Events.

مثال:

```text
Reserve Last Seat
→ يحتاج Strong Consistency

Send Enrollment Notification
→ يمكن أن تكون Asynchronous
```

تفاصيل الـTransactions والـEvents والـOutbox تتحسم في قرارات تقنية مستقلة.

---

## 7. الـDomain والـInfrastructure

نستفيد من مبادئ Clean Architecture بدون فرض Layers مستقلة على مستوى النظام كله.

القواعد:

```text
Domain
→ لا تعتمد على EF Core أو Infrastructure

Infrastructure
→ تنفذ Persistence / Providers

API
→ Host / Composition Root

Features
→ تنظم الـUse Cases
```

والتنظيم الأساسي يظل:

```text
Module
↓
Feature
↓
Domain / Infrastructure حسب الحاجة
```

---

## 8. حاجات مش هنبدأ بيها

في V1 لا نبدأ بـ:

```text
Microservices
Database per Module
Message Broker
Event Sourcing
Distributed Transactions
Separate Read/Write Databases
Generic Repository
Generic Base Service
DDD لكل جدول
Plugin Architecture
```

أي واحدة منهم تحتاج مشكلة حقيقية وقرار تقني مستقل قبل إضافتها.

---

## 9. قواعد أساسية

1. كل Business Capability لها Module مالكة واضحة.
2. كل Use Case تتنظم كـVertical Slice.
3. الـEndpoints تظل خفيفة.
4. الـBusiness Rules المعقدة تظل داخل Domain/Application Logic المناسب.
5. ممنوع تعديل بيانات Module أخرى مباشرة.
6. Shared Building Blocks تظل صغيرة ولا تحتوي Business Entities.
7. ممنوع Circular Dependencies بين Modules.
8. Architecture Boundaries تتغطى بـArchitecture Tests.
9. Events تستخدم عند وجود قيمة فعلية، مش لمجرد تطبيق Pattern.
10. فصل Module إلى Service مستقلة مستقبلًا يتم فقط عند وجود سبب تشغيلي حقيقي.

---

## القرار النهائي المختصر

> EduCenterOS هو **Modular Monolith** منظم باستخدام **Vertical Slice Architecture** و**Pragmatic DDD**.

> نبدأ بتطبيق وDeployment وقاعدة بيانات واحدة، مع حدود قوية بين الـBusiness Modules.

> لا نستخدم Microservices أو Message Broker في البداية، لكن يمكن استخدام **Domain Events وIntegration Events وTransactional Outbox** داخل الـMonolith عند الحاجة.

> Strong Business Invariants تظل متزامنة، بينما الـSide Effects التي تقبل التأخير يمكن تنفيذها Asynchronously.