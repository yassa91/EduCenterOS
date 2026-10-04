using EduCenterOS.BuildingBlocks.Results;
using EduCenterOS.Modules.IdentityAccess.Domain;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenterOS.Modules.IdentityAccess;

public static partial class ModuleRegistration
{
    private static void AddAccountAuthentication(IServiceCollection services, Func<HttpContext, Error, Task> writeError)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentAccountActor, CurrentAccountActor>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddAuthorization(options => options.AddPolicy("AccountSelf", policy => policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme).RequireAuthenticatedUser()));
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<AuthenticationTokens>((options, tokens) =>
        {
            options.MapInboundClaims = false;
            options.SaveToken = false;
            options.IncludeErrorDetails = false;
            options.TokenValidationParameters = tokens.ValidationParameters();
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    if (context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<SecurityEndpointMetadata>()?.Classification != "AccountSelf")
                    {
                        context.NoResult();

                        return Task.CompletedTask;
                    }

                    var headers = context.Request.Headers.Authorization;

                    if (headers.Count == 0)
                    {
                        context.NoResult();

                        return Task.CompletedTask;
                    }

                    if (
                        !context.Request.IsHttps ||
                        headers.Count != 1 ||
                        headers[0] is not { } header ||
                        header.Length is < 8 or > 8199 ||
                        !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ||
                        header[7..].Any(char.IsWhiteSpace)
                    )
                    {
                        context.Fail("Authentication.Rejected");

                        return Task.CompletedTask;
                    }

                    context.Token = header[7..];

                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    var encoded = context.Request.Headers.Authorization.ToString()[7..];

                    if (!tokens.TryReadActor(encoded, out var actor)) context.Fail("Authentication.Rejected");
                    else context.HttpContext.Items[CurrentAccountActor.CryptographicItem] = actor;

                    return Task.CompletedTask;
                },
                OnChallenge = async context =>
                {
                    context.HandleResponse();
                    await writeError(context.HttpContext, AuthenticationErrors.Rejected);
                },
                OnForbidden = context => writeError(context.HttpContext, AuthenticationErrors.BrowserRejected)
            };
        });
    }
}
