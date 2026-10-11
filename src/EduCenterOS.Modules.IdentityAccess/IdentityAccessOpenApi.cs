using System.Text.Json.Nodes;
using EduCenterOS.Modules.IdentityAccess.Features.Login;
using EduCenterOS.Modules.IdentityAccess.Features.ListSessions;
using EduCenterOS.Modules.IdentityAccess.Features.Logout;
using EduCenterOS.Modules.IdentityAccess.Features.LogoutAll;
using EduCenterOS.Modules.IdentityAccess.Features.RevokeSession;
using EduCenterOS.Modules.IdentityAccess.Features.Refresh;
using EduCenterOS.Modules.IdentityAccess.Features.Shared.Authentication;
using EduCenterOS.Modules.IdentityAccess.Contracts;
using EduCenterOS.Modules.IdentityAccess.Features.RegisterAccount;
using EduCenterOS.Modules.IdentityAccess.Features.RequestPhoneVerification;
using EduCenterOS.Modules.IdentityAccess.Features.ResendPhoneVerification;
using EduCenterOS.Modules.IdentityAccess.Features.VerifyPhone;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace EduCenterOS.Modules.IdentityAccess;

public static partial class ModuleRegistration
{
    public static void ConfigureIdentityAccessOpenApi(this OpenApiOptions options)
    {
        options.AddDocumentTransformer((document, context, cancellation) =>
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "educenteros-access+jwt",
                Description = "Access JWT from Login/Refresh; keep only in browser memory. Refresh cookie alone is not authorization."
            };

            return Task.CompletedTask;
        });
        options.AddSchemaTransformer((schema, context, cancellation) =>
        {
            var type = context.JsonTypeInfo.Type;

            if (type == typeof(LoginRequest))
            {
                Strict(schema);
                schema.Properties!["phoneNumber"] = Text(1, 32, "Verified Egyptian mobile number; S02 normalization.");
                var password = Text(1, 128, "No trim; 1–128 UTF-16 units without controls. Historical passwords do not use the registration minimum.");
                password.Format = "password";
                password.WriteOnly = true;
                Utf16Bounds(password, 1, 128);
                schema.Properties!["password"] = password;
            }
            else if (
                type == typeof(RefreshRequest) ||
                type == typeof(LogoutRequest) ||
                type == typeof(LogoutAllRequest) ||
                type == typeof(RevokeSessionRequest)
            ) Strict(schema);
            else if (type == typeof(SessionPagination))
            {
                schema.Properties!["type"] = Text(4, 4, "Page-based pagination.", "^page$");
                schema.Properties!["page"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Minimum = "1", Maximum = "2147483647" };
                schema.Properties!["pageSize"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Minimum = "1", Maximum = "100" };
                schema.Description = "Defaults page=1/pageSize=20; owner-filtered count and bounded page are separate read snapshots and may drift during concurrent session changes.";
            }
            else if (type == typeof(AccessResponse))
            {
                schema.Properties!["accessToken"].Description = "Short-lived access JWT; browser memory only; never persist or log.";
            }
            else if (type == typeof(PhoneVerificationRequest))
            {
                Strict(schema);
                schema.Properties!["phoneNumber"] = Text(1, 32, "Egyptian mobile number: trim then normalize local/0020/+20 ASCII forms.");
            }
            else if (type == typeof(ResendPhoneVerificationRequest)) Strict(schema);
            else if (type == typeof(VerifyPhoneRequest))
            {
                Strict(schema);
                schema.Properties!["code"] = Text(6, 6, "Six ASCII digits; never logged.", "^[0-9]{6}$");
            }
            else if (type == typeof(RegisterAccountRequest))
            {
                var policy = context.ApplicationServices.GetRequiredService<IdentityAccessRuntimeSettings>().Policy;
                Strict(schema);
                schema.Properties!["verificationProof"] = Text(43, 43, "Canonical unpadded Base64url of 32 bytes, bound to this challenge; consumed once.", "^[A-Za-z0-9_-]{42}[AEIMQUYcgkosw048]$");
                var name = Text(1, 200, "Trim and NFC; 2–200 UTF-16 units; no control characters. JSON Schema counts Unicode code points; UTF-16 limits are authoritative.");
                Utf16Bounds(name, 2, 200);
                schema.Properties!["fullName"] = name;
                var password = Text(
                    (policy.PasswordMinimumLength + 1) / 2,
                    policy.PasswordMaximumLength,
                    "No trim/normalization; configured UTF-16 bounds are authoritative; JSON Schema counts Unicode code points. No control characters."
                );
                Utf16Bounds(password, policy.PasswordMinimumLength, policy.PasswordMaximumLength);
                password.Format = "password";
                password.WriteOnly = true;
                schema.Properties!["password"] = password;
                var email = Text(3, 254, "Optional/null; trim/lowercase ASCII dotted-domain email; local part at most 64; remains unverified.");
                email.Type = JsonSchemaType.String | JsonSchemaType.Null;
                schema.Properties!["emailAddress"] = email;
                schema.Properties!["challengeId"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid", Description = "Nonempty challenge UUID returned by Request. Phone is read from the verified challenge." };
            }
            else if (type == typeof(VerifyPhoneResponse)) schema.Properties!["verificationProof"] = Text(43, 43, "Temporary proof returned once; never include it in logs or shared examples.");

            return Task.CompletedTask;
        });
        options.AddOperationTransformer((operation, context, cancellation) =>
        {
            var metadata = context.Description.ActionDescriptor.EndpointMetadata.OfType<SecurityEndpointMetadata>().SingleOrDefault();

            if (metadata is null) return Task.CompletedTask;

            operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();
            operation.Extensions["x-security-classification"] = new JsonNodeExtension(JsonValue.Create(metadata.Classification));
            operation.Extensions["x-idempotency-mode"] = new JsonNodeExtension(JsonValue.Create(metadata.IdempotencyMode));
            operation.Extensions["x-rate-limit-policy"] = new JsonNodeExtension(JsonValue.Create(metadata.PolicyName));
            operation.Security = metadata.Classification == "AccountSelf"
                ? [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = [] }]
                : [];

            if (metadata.BrowserProtected)
            {
                operation.Parameters ??= [];
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = "X-EduCenterOS-Auth", In = ParameterLocation.Header, Required = true,
                    Description = "Browser CSRF marker; value 1. HTTPS and exact automatic browser Origin are required.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String, Pattern = "^1$", Default = JsonValue.Create("1") }
                });
            }

            if (context.Description.RelativePath?.Contains("{challengeId}", StringComparison.Ordinal) == true)
            {
                operation.Parameters ??= [];
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = "challengeId",
                    In = ParameterLocation.Path,
                    Required = true,
                    Description = "Nonempty challenge UUID in canonical hyphenated form.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" }
                });
            }

            if (context.Description.RelativePath?.Contains("{sessionId}", StringComparison.Ordinal) == true)
            {
                operation.Parameters ??= [];
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = "sessionId",
                    In = ParameterLocation.Path,
                    Required = true,
                    Description = "Nonempty lowercase canonical UUID of an owned session; missing and foreign targets share 404.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid", Pattern = "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$" }
                });
            }

            if (context.Description.RelativePath == "api/v1/auth/sessions")
            {
                operation.Parameters ??= [];
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = "page", In = ParameterLocation.Query,
                    Description = "Single positive ASCII integer; default 1. Unknown/duplicate query fields are rejected.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Minimum = "1", Maximum = "2147483647", Default = JsonValue.Create(1) }
                });
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = "pageSize", In = ParameterLocation.Query,
                    Description = "Single ASCII integer 1–100; default 20. History includes expired and revoked sessions; createdAtUtc/id descending.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Minimum = "1", Maximum = "100", Default = JsonValue.Create(20) }
                });
            }

            if (operation.RequestBody is OpenApiRequestBody body) body.Required = true;

            foreach (var response in operation.Responses!.Values.OfType<OpenApiResponse>())
            {
                response.Headers ??= new Dictionary<string, IOpenApiHeader>();
                response.Headers["X-Correlation-Id"] = new OpenApiHeader { Description = "Server-generated correlation identifier.", Schema = Text(32, 32, "Correlation identifier.") };
                response.Headers["Cache-Control"] = new OpenApiHeader { Description = "Sensitive responses use no-store.", Schema = new OpenApiSchema { Type = JsonSchemaType.String, Pattern = "^no-store$" } };
            }

            if (metadata.BrowserProtected && operation.Responses.TryGetValue("200", out var success) && success is OpenApiResponse cookieResponse)
                cookieResponse.Headers!["Set-Cookie"] = new OpenApiHeader { Description = "Secure HttpOnly SameSite=Strict refresh cookie; Path=/api/v1/auth; no Domain. Never returned in JSON.", Schema = new OpenApiSchema { Type = JsonSchemaType.String } };

            if (
                metadata.BrowserProtected &&
                operation.Responses.TryGetValue("204", out var noContent) &&
                noContent is OpenApiResponse deletionResponse
            )
                deletionResponse.Headers!["Set-Cookie"] = new OpenApiHeader { Description = "Matching refresh-cookie deletion after confirmed success; target revoke clears only an identifiable cookie belonging to the target session.", Schema = new OpenApiSchema { Type = JsonSchemaType.String } };

            if (operation.Responses.TryGetValue("401", out var unauthorized) && unauthorized is OpenApiResponse unauthorizedResponse)
                unauthorizedResponse.Headers!["WWW-Authenticate"] = new OpenApiHeader { Description = "Bearer challenge without account details.", Schema = new OpenApiSchema { Type = JsonSchemaType.String, Pattern = "^Bearer$" } };

            foreach (var status in new[] { "429", "503" })
                if (operation.Responses.TryGetValue(status, out var response) && response is OpenApiResponse concrete)
                    concrete.Headers!["Retry-After"] = new OpenApiHeader { Description = "Optional positive delay-seconds only when a known limiter/confirmed rollback supplies a wait; absence does not permit credential replay.", Schema = new OpenApiSchema { Type = JsonSchemaType.String, Pattern = "^[1-9][0-9]*$" } };

            return Task.CompletedTask;
        });
    }

    private static OpenApiSchema Text(int min, int max, string description, string? pattern = null) => new()
    { Type = JsonSchemaType.String, MinLength = min, MaxLength = max, Description = description, Pattern = pattern };

    private static void Utf16Bounds(OpenApiSchema schema, int minimum, int maximum)
    {
        schema.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        schema.Extensions["x-min-length-utf16"] = new JsonNodeExtension(JsonValue.Create(minimum));
        schema.Extensions["x-max-length-utf16"] = new JsonNodeExtension(JsonValue.Create(maximum));
    }

    private static void Strict(OpenApiSchema schema)
    {
        schema.AdditionalPropertiesAllowed = false;
        schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
        schema.Description = "Strict JSON object; camelCase/case-sensitive; reject unknown/duplicate fields and query parameters. Body limit 16,384 bytes.";
    }
}
