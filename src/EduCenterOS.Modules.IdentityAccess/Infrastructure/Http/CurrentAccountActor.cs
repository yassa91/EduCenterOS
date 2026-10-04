using EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;
using Microsoft.AspNetCore.Http;

namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;

internal interface ICurrentAccountActor
{
    AuthenticatedActor Require();
}

internal sealed class CurrentAccountActor(IHttpContextAccessor accessor) : ICurrentAccountActor
{
    internal static readonly object CryptographicItem = new();
    internal static readonly object AuthoritativeItem = new();

    public AuthenticatedActor Require() => accessor.HttpContext?.Items[AuthoritativeItem] is AuthenticatedActor actor
        ? actor : throw new InvalidOperationException("Security.CurrentActorUnavailable");
}
