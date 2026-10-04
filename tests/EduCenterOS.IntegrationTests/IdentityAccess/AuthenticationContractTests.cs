using System.Net;
using System.Text.Json;
using EduCenterOS.IntegrationTests.Api;
using EduCenterOS.IntegrationTests.Infrastructure;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EduCenterOS.IntegrationTests.IdentityAccess;

public sealed class AuthenticationContractTests(OwnedPostgresFixture database) : IClassFixture<OwnedPostgresFixture>
{
    [Fact]
    public async Task OpenApiAndInventory_ExposeOnlyCompletedRoutesAndStrictLoginContract()
    {
        await using var factory = new TestingApiFactory(database);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var paths = doc.RootElement.GetProperty("paths");
        Assert.Equal(new[] { "/api/v1/accounts", "/api/v1/accounts/me", "/api/v1/auth/login", "/api/v1/auth/refresh", "/api/v1/phone-verifications", "/api/v1/phone-verifications/{challengeId}/resend", "/api/v1/phone-verifications/{challengeId}/verify" }, paths.EnumerateObject().Select(path => path.Name).Order(StringComparer.Ordinal).ToArray());
        var login = paths.GetProperty("/api/v1/auth/login").GetProperty("post");
        Assert.Equal("Authentication_Login", login.GetProperty("operationId").GetString());
        Assert.Equal("AnonymousSecurity", login.GetProperty("x-security-classification").GetString());
        Assert.Equal("SecuritySpecific", login.GetProperty("x-idempotency-mode").GetString());
        Assert.Equal("AuthenticationSource", login.GetProperty("x-rate-limit-policy").GetString());
        Assert.Empty(login.GetProperty("security").EnumerateArray());
        var header = Assert.Single(login.GetProperty("parameters").EnumerateArray());
        Assert.Equal("X-EduCenterOS-Auth", header.GetProperty("name").GetString());
        Assert.Equal("header", header.GetProperty("in").GetString());
        Assert.True(header.GetProperty("required").GetBoolean());
        Assert.Equal("1", header.GetProperty("schema").GetProperty("default").GetString());
        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");
        var request = schemas.GetProperty("LoginRequest");
        Assert.False(request.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(new[] { "password", "phoneNumber" }, request.GetProperty("required").EnumerateArray().Select(value => value.GetString()).Order(StringComparer.Ordinal).ToArray());
        var password = request.GetProperty("properties").GetProperty("password");
        Assert.True(password.GetProperty("writeOnly").GetBoolean());
        Assert.Equal(1, password.GetProperty("x-min-length-utf16").GetInt32());
        Assert.Equal(128, password.GetProperty("x-max-length-utf16").GetInt32());
        Assert.Equal(new[] { "accessToken", "expiresAtUtc", "tokenType" }, schemas.GetProperty("AccessResponse").GetProperty("properties").EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        var statuses = login.GetProperty("responses");
        Assert.True(statuses.GetProperty("200").GetProperty("headers").TryGetProperty("Set-Cookie", out _));
        Assert.True(statuses.GetProperty("401").GetProperty("headers").TryGetProperty("WWW-Authenticate", out _));
        foreach (var status in new[] { "400", "401", "403", "406", "413", "415", "429", "500", "503" })
        {
            Assert.True(statuses.GetProperty(status).GetProperty("content").TryGetProperty("application/problem+json", out _));
            Assert.True(statuses.GetProperty(status).GetProperty("headers").TryGetProperty("Cache-Control", out _));
        }
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/v1/", StringComparison.Ordinal) == true).ToArray();
        Assert.Equal(7, endpoints.Length);
        var refresh = paths.GetProperty("/api/v1/auth/refresh").GetProperty("post");
        Assert.Equal("Authentication_Refresh", refresh.GetProperty("operationId").GetString());
        Assert.Equal("AnonymousSecurity", refresh.GetProperty("x-security-classification").GetString());
        Assert.Equal("RefreshSource", refresh.GetProperty("x-rate-limit-policy").GetString());
        Assert.Equal("SecuritySpecific", refresh.GetProperty("x-idempotency-mode").GetString());
        Assert.Empty(refresh.GetProperty("security").EnumerateArray());
        Assert.True(refresh.GetProperty("requestBody").GetProperty("required").GetBoolean());
        Assert.False(schemas.GetProperty("RefreshRequest").GetProperty("additionalProperties").GetBoolean());
        if (schemas.GetProperty("RefreshRequest").TryGetProperty("properties", out var refreshProperties))
            Assert.Empty(refreshProperties.EnumerateObject());
        var current = paths.GetProperty("/api/v1/accounts/me").GetProperty("get");
        Assert.Equal("AccountSelf", current.GetProperty("x-security-classification").GetString());
        Assert.Equal("NotRequiredRead", current.GetProperty("x-idempotency-mode").GetString());
        var requirement = Assert.Single(current.GetProperty("security").EnumerateArray());
        Assert.Empty(requirement.GetProperty("Bearer").EnumerateArray());
        var scheme = doc.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
        Assert.Equal(new[] { "createdAtUtc", "fullName", "personIdentityId", "userAccountId" }, schemas.GetProperty("CurrentAccountResponse").GetProperty("properties").EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        var protectedEndpoint = Assert.Single(endpoints, candidate => candidate.RoutePattern.RawText == "/api/v1/accounts/me");
        Assert.Null(protectedEndpoint.Metadata.GetMetadata<IAllowAnonymous>());
        Assert.Equal("AccountSelf", protectedEndpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Single().Policy);
        Assert.False(protectedEndpoint.Metadata.GetMetadata<SecurityEndpointMetadata>()!.BrowserProtected);
        var endpoint = Assert.Single(endpoints, candidate => candidate.RoutePattern.RawText == "/api/v1/auth/login");
        Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
        Assert.True(endpoint.Metadata.GetMetadata<SecurityEndpointMetadata>()!.BrowserProtected);
        Assert.Equal(new[] { "POST" }, endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);
    }
}
