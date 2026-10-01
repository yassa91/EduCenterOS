# T34 - Logging & Observability

## القرار وحالته

```text
Status: Approved implementation baseline
Scope: S01 foundation; extended per active Feature
```

نعتمد `Microsoft.Extensions.Logging` مع structured JSON Console logs وW3C `Activity` المتاحة من ASP.NET Core، دون logging framework إضافية أو collector خارجي في التأسيس. هذه وثيقة تصميم؛ التشغيل والاختبارات تثبت في S01-T04–T06.

## 1. الملكية والحدود

- T31/T32 تملك HTTP headers وProblemDetails وstable error codes.
- T35 تملك minimum security-event contract وredaction obligations.
- T36 تملك configuration والأسرار؛ لا تعرض effective configuration في logs.
- T34 تملك technical event shape وsink وcorrelation وhealth contracts.
- Business Audit سجل مستقل حسب Business 11 وقرار Audit المالك؛ console logs لا تستبدل transactional audit/outbox.
- التشغيل المحلي والـCI وفق T37/T38؛ public exposure والـremote observability/retention تتحدد في T39 قبل deployment.

## 2. Logging baseline

الـAPI Composition Root تسجل JSON Console provider فقط، مع UTC timestamps وstructured scopes. Default level هي `Information`، وframework request/hosting categories تقلل إلى `Warning` حتى لا تطبع URLs/queries عبر request lifecycle messages الافتراضية.

تستخدم ثابتة `EventId` وmessage templates ثابتة. الحد الأدنى للحدث التقني المنطبق:

```text
event code / eventId
occurredAtUtc (console timestamp)
level + category
correlationId when there is an HTTP request
traceId when Activity exists
safe outcome / reason code
```

لا raw exception object أو `Exception.Message`/`ToString()` في baseline logs. Unexpected failure تسجل stable error/event code وexception type الموثوقة فقط عند الحاجة، دون message/stack/data. Health/DB/provider adapter لا تسرب تفاصيل الفشل عبر default framework logger.

## 3. HTTP correlation

كل request تولد server-owned correlation ID: 32 lowercase hexadecimal characters من UUID عشوائية، وترد في `X-Correlation-Id` بما فيها error responses. لا نعتمد client correlation header في V1؛ تجاهلها يمنع unbounded/unsafe values.

ProblemDetails تستخدم `correlationId` بنفس القيمة. `traceId` من W3C Activity الحالية، إن وجدت، قيمة منفصلة ولا تستخدم للـauthorization أو tenant/idempotency decisions.

Correlation middleware تسبق response/error handling، وتضيف header قبل إرسال الاستجابة وتفتح structured logging scope. عدم وجود HTTP request في startup/background diagnostics لا يولد actor أو tenant وهمية.

## 4. Data minimization وsafe events

لا نسجل request/response bodies أو raw URLs/query strings أو arbitrary route values أو headers أو connection strings أو passwords/tokens/cookies/OTP/keys أو SQL parameter values. Request completion، إذا سجلت، تستخدم method وmatched route template وstatus وduration فقط.

لا EF sensitive-data logging في dev/staging/prod. أسماء الخيارات مع violated rule يجوز عرضها دون values. No configuration dumps، لا caller-controlled metric labels، ولا user-supplied text في message templates.

Security events تضيف actor/operation/resource references فقط عند وجود Feature تحتاجها وفق T35. لا تنفذ authentication events أو metrics عن عمليات غير موجودة لمجرد إكمال catalog.

## 5. Operational health contracts

```text
GET /health/live   → process can handle HTTP
GET /health/ready  → startup complete + all active required dependencies healthy
```

- Healthy: `200` وplain text `Healthy`.
- Required dependency unavailable/degraded: readiness `503` وplain text `Unhealthy`.
- liveness لا تفحص DB أو Infisical.
- readiness تفحص DB بـbounded connection/`SELECT 1` عندما تصبح required dependency في S01-T05. Missing critical startup config تفشل startup أصلًا وفق T36.
- timeout النهائي يسجل في owning options مع اختباره؛ لا migrations أو writes أو destructive effects داخل probes.
- Infisical fetch عند bootstrap فقط. Outage بعد نجاح تحميل startup snapshot لا يجعل readiness تفشل لمجرد تعذر الاتصال بالمزود، وفق T36.
- استجابات probes لا تعرض check names أو exception descriptions أو topology؛ وتستخدم `Cache-Control: no-store` وCorrelation ID.
- operational routes خارج `/api/v1` وتستبعد من public OpenAPI؛ لا تصنف كBusiness/public-data endpoints.
- S01 تشغل listener محلية على loopback فقط، والاختبارات TestServer. Host header وحدها ليست شبكة موثوقة. Exposure في staging/production لا يفعل قبل قرار T39 وحماية management access المناسبة.

قبل إضافة DB dependency، readiness تثبت Host startup فقط. هذا لا يثبت persistence أو readiness لFeature مستقبلية.

## 6. Failure semantics والاختبارات

Technical sink failure ليست دليل rollback ولا سببًا لتزوير business outcome. Mandatory audit/outbox تتبع transaction المالكة؛ لا تعتمد صحة commit على console event.

S01-T06 تثبت: server correlation حتى مع client header ضارة، تطابق header/error body، الفصل عن traceId، sanitized unexpected error، health status/body/no-store، DB outage مقابل liveness، وغياب synthetic secret markers من logs/responses. Fixture تستخدم capture provider محدودًا دون تغيير production behavior.

Framework/provider categories التي يمكنها عرض exception details تراجع في S01-T04، وتفلتر أو تستبدل الرسائل الحساسة؛ رفع verbosity يحتاج مراجعة redaction مستقلة قبل الاعتماد.

S01 baseline تعطل default logs الخاصة بـASP.NET diagnostics وKestrel وhealth-check service وSystem.Net.Http لأنها قد تحتوي exception/URL details. الـapplication error boundary تسجل sanitized failure code/type؛ لا raw exception object. هذه filters لا تغير فشل startup أو response outcome.

## 7. ما يؤجل حتى الحاجة

OpenTelemetry exporters وremote log store وdashboards وalert routing وmetrics catalog وsampling/retention لا تضيفها S01. ضوابط عدم التسريب هنا تظل ملزمة لأي sink لاحقة.

## مراجع التنفيذ

- [Microsoft: Logging in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/logging/?view=aspnetcore-10.0).
- [Microsoft: Health checks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0).

اختيار JSON Console وحدود التشغيل والسياسات الرقمية/الأمنية هنا قرارات المشروع؛ لا ندعي أن framework توفر redaction تلقائيًا.
