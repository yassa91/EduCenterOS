using System.Text;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EduCenterOS.IntegrationTests.Api;
using EduCenterOS.IntegrationTests.Infrastructure;
using EduCenterOS.Modules.IdentityAccess.Domain;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace EduCenterOS.IntegrationTests.IdentityAccess;

internal static class AuthenticationTestSupport
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    internal const string Password = " Synthetic-authentication-password ";

    internal static HttpClient Browser(TestingApiFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(TestingApiFactory.AuthenticationOrigin),
            HandleCookies = false
        });
        client.DefaultRequestHeaders.Add("Origin", TestingApiFactory.AuthenticationOrigin);
        client.DefaultRequestHeaders.Add("X-EduCenterOS-Auth", "1");

        return client;
    }

    internal static async Task PrepareAsync(OwnedPostgresFixture database)
    {
        await database.PrepareIdentityAsync();
        await database.ResetIdentityAsync();
    }

    internal static IdentityAccessDbContext Context(OwnedPostgresFixture database) => new(IdentityAccessDbContext.Options(database.ModuleConnectionString));

    internal static async Task<Guid> SeedAsync(OwnedPostgresFixture database, TestingApiFactory factory, string phone = "01012345678", string password = Password, int? legacyIterations = null, DateTimeOffset? createdAt = null, string fullName = "مستخدم للاختبار")
    {
        using var scope = factory.Services.CreateScope();
        var hasher = legacyIterations is null ? scope.ServiceProvider.GetRequiredService<IPasswordHasher<UserAccount>>()
            : new PasswordHasher<UserAccount>(Options.Create(new PasswordHasherOptions { CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3, IterationCount = legacyIterations.Value }));
        var now = createdAt ?? factory.Clock.UtcNow;
        var person = new PersonIdentity(Guid.CreateVersion7(now), fullName, now);
        var account = new UserAccount(Guid.CreateVersion7(now), person.Id, RegistrationInputs.Phone(phone).Value, null, hasher.HashPassword(null!, password), now, now);
        await using var context = Context(database);
        context.People.Add(person);
        context.Accounts.Add(account);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return account.Id;
    }

    internal static Task<HttpResponseMessage> LoginAsync(HttpClient client, string phone = "01012345678", string password = Password) =>
        client.PostAsJsonAsync("/api/v1/auth/login", new { phoneNumber = phone, password }, TestContext.Current.CancellationToken);

    internal static async Task<HttpResponseMessage> MeAsync(HttpClient client, string access, string route = "/api/v1/accounts/me")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + access);

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    internal static async Task<HttpResponseMessage> RefreshAsync(HttpClient client, string? raw, string body = "{}", string? access = null, string route = "/api/v1/auth/refresh", CancellationToken? cancellation = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, route);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        if (raw is not null) request.Headers.TryAddWithoutValidation("Cookie", "__Secure-educenteros-refresh=" + raw);
        if (access is not null) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + access);

        return await client.SendAsync(request, cancellation ?? TestContext.Current.CancellationToken);
    }

    internal static async Task<HttpResponseMessage> CommandAsync(HttpClient client, string route, string? access, string? refresh, string body = "{}", bool marker = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, route);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("Origin", TestingApiFactory.AuthenticationOrigin);
        if (marker) request.Headers.Add("X-EduCenterOS-Auth", "1");
        if (access is not null) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + access);
        if (refresh is not null) request.Headers.TryAddWithoutValidation("Cookie", "__Secure-educenteros-refresh=" + refresh);

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    internal static async Task<TestAuthenticationGrant> ReadGrantAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(3, body.RootElement.EnumerateObject().Count());
        Assert.Equal("Bearer", body.RootElement.GetProperty("tokenType").GetString());
        var cookie = SetCookieHeaderValue.Parse(response.Headers.GetValues("Set-Cookie").Single());
        Assert.Equal(AuthenticationCookie.Name, cookie.Name.Value);
        Assert.True(cookie.HttpOnly && cookie.Secure);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Strict, cookie.SameSite);
        Assert.Equal(AuthenticationCookie.Path, cookie.Path.Value);
        Assert.False(cookie.Domain.HasValue);

        return new TestAuthenticationGrant(body.RootElement.GetProperty("accessToken").GetString()!, cookie.Value.Value!, body.RootElement.GetProperty("expiresAtUtc").GetDateTimeOffset());
    }

    internal static async Task AssertRejectedAsync(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.Unauthorized, string code = "IdentityAccess.AuthenticationRejected")
    {
        Assert.Equal(status, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());

        if (status == HttpStatusCode.Unauthorized) Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
    }
}

// No generated diagnostic ToString containing credentials.
internal sealed class TestAuthenticationGrant(string access, string refresh, DateTimeOffset expiresAtUtc)
{
    internal string Access { get; } = access;
    internal string Refresh { get; } = refresh;
    internal DateTimeOffset ExpiresAtUtc { get; } = expiresAtUtc;
}
