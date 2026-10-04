using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EduCenterOS.IntegrationTests.Api;
using EduCenterOS.IntegrationTests.Infrastructure;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EduCenterOS.IntegrationTests.IdentityAccess;

public sealed class RegistrationContractTests(OwnedPostgresFixture database) : IClassFixture<OwnedPostgresFixture>
{
    [Fact]
    public async Task OpenApi_MatchesRoutesSchemasPoliciesAndSafeProblemContract()
    {
        await database.PrepareIdentityAsync();
        await using var factory = new TestingApiFactory(database);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var paths = doc.RootElement.GetProperty("paths");
        Assert.Equal(4, paths.EnumerateObject().Count());
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in paths.EnumerateObject())
        {
            Assert.StartsWith("/api/v1/", path.Name);
            var operation = path.Value.GetProperty("post");
            var id = operation.GetProperty("operationId").GetString()!;
            Assert.Matches("^[A-Za-z]+_[A-Za-z]+$", id);
            Assert.True(ids.Add(id));
            Assert.Equal("AnonymousSecurity", operation.GetProperty("x-security-classification").GetString());
            Assert.Equal("SecuritySpecific", operation.GetProperty("x-idempotency-mode").GetString());
            Assert.Empty(operation.GetProperty("security").EnumerateArray());
            Assert.True(operation.GetProperty("requestBody").GetProperty("required").GetBoolean());
            Assert.True(operation.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema").TryGetProperty("$ref", out _));

            if (path.Name.Contains("{challengeId}", StringComparison.Ordinal))
            {
                var parameter = Assert.Single(operation.GetProperty("parameters").EnumerateArray());
                Assert.Equal("challengeId", parameter.GetProperty("name").GetString());
                Assert.Equal("path", parameter.GetProperty("in").GetString());
                Assert.True(parameter.GetProperty("required").GetBoolean());
                Assert.Equal("uuid", parameter.GetProperty("schema").GetProperty("format").GetString());
            }

            var statuses = operation.GetProperty("responses");
            var expected = new[] { "400", "406", "413", "415", "422", "429", "500", "503", path.Name == "/api/v1/accounts" ? "201" : "200" };

            foreach (var status in expected) Assert.True(statuses.TryGetProperty(status, out _), $"Missing {path.Name} status {status}");

            if (path.Name == "/api/v1/accounts") Assert.True(statuses.TryGetProperty("409", out _));

            foreach (var status in statuses.EnumerateObject())
            {
                Assert.True(status.Value.GetProperty("headers").TryGetProperty("Cache-Control", out _));
                Assert.True(status.Value.GetProperty("headers").TryGetProperty("X-Correlation-Id", out _));

                if (int.Parse(status.Name, System.Globalization.CultureInfo.InvariantCulture) >= 400) Assert.True(status.Value.GetProperty("content").TryGetProperty("application/problem+json", out _));
            }
        }

        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");

        foreach (var name in new[] { "PhoneVerificationRequest", "ResendPhoneVerificationRequest", "VerifyPhoneRequest", "RegisterAccountRequest" }) Assert.False(schemas.GetProperty(name).GetProperty("additionalProperties").GetBoolean());

        var register = schemas.GetProperty("RegisterAccountRequest");
        Assert.Equal(new[] { "challengeId", "fullName", "password", "verificationProof" }, register.GetProperty("required").EnumerateArray().Select(value => value.GetString()).Order(StringComparer.Ordinal).ToArray());
        var props = register.GetProperty("properties");
        Assert.Equal(5, props.EnumerateObject().Count());
        Assert.False(props.TryGetProperty("phoneNumber", out _));
        Assert.True(props.GetProperty("password").GetProperty("writeOnly").GetBoolean());
        Assert.Equal(6, props.GetProperty("password").GetProperty("minLength").GetInt32());
        Assert.Equal(12, props.GetProperty("password").GetProperty("x-min-length-utf16").GetInt32());
        Assert.Equal(1, props.GetProperty("fullName").GetProperty("minLength").GetInt32());
        Assert.Equal(2, props.GetProperty("fullName").GetProperty("x-min-length-utf16").GetInt32());
        Assert.Equal(128, props.GetProperty("password").GetProperty("maxLength").GetInt32());
        Assert.Equal(43, props.GetProperty("verificationProof").GetProperty("maxLength").GetInt32());
        var problem = schemas.GetProperty("ProblemDetails").GetProperty("properties");
        Assert.Equal("integer", problem.GetProperty("status").GetProperty("type").GetString());
        Assert.True(problem.TryGetProperty("code", out _));
        Assert.True(problem.TryGetProperty("correlationId", out _));
        Assert.True(problem.TryGetProperty("errorsTruncated", out _));
        var issues = problem.GetProperty("errors").GetProperty("additionalProperties");
        Assert.Equal("array", issues.GetProperty("type").GetString());
        Assert.Equal(new[] { "code", "description" }, issues.GetProperty("items").GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/v1/", StringComparison.Ordinal) == true).ToArray();
        Assert.Equal(4, endpoints.Length);

        foreach (var endpoint in endpoints)
        {
            Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
            var metadata = endpoint.Metadata.GetMetadata<SecurityEndpointMetadata>();
            Assert.NotNull(metadata);
            Assert.Equal("AnonymousSecurity", metadata.Classification);
            Assert.Equal("SecuritySpecific", metadata.IdempotencyMode);
            Assert.Equal(new[] { "POST" }, endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);
        }
    }

    [Fact]
    public async Task TransportAndRouteFailures_UseSafeEnvelopeAndEnforceNegotiation()
    {
        await database.PrepareIdentityAsync();
        await using var factory = new TestingApiFactory(database);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/phone-verifications") { Content = JsonContent.Create(new { phoneNumber = "01012345678" }) };
        request.Headers.Accept.ParseAdd("text/html");
        using var unacceptable = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotAcceptable, unacceptable.StatusCode);
        using var invalidId = await client.PostAsJsonAsync("/api/v1/phone-verifications/not-a-uuid/verify", new { code = "123456" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalidId.StatusCode);
        using var zeroId = await client.PostAsJsonAsync($"/api/v1/phone-verifications/{Guid.Empty}/resend", new { }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, zeroId.StatusCode);
        using var emptyResend = await client.PostAsync(
            $"/api/v1/phone-verifications/{Guid.CreateVersion7()}/resend",
            new StringContent("{}", Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken
        );
        Assert.Equal((HttpStatusCode)422, emptyResend.StatusCode);

        foreach (var response in new[] { unacceptable, invalidId, zeroId, emptyResend })
        {
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal((int)response.StatusCode, body.RootElement.GetProperty("status").GetInt32());
            Assert.Equal(response.Headers.GetValues("X-Correlation-Id").Single(), body.RootElement.GetProperty("correlationId").GetString());
            Assert.Empty(response.Headers.WwwAuthenticate);
            Assert.False(body.RootElement.TryGetProperty("instance", out _));
        }
    }
}
