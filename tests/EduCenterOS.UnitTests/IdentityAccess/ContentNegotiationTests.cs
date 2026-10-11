using EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EduCenterOS.UnitTests.IdentityAccess;

public sealed class ContentNegotiationTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("application/json", null)]
    [InlineData("APPLICATION/JSON", null)]
    [InlineData("Application/Json", null)]
    [InlineData("APPLICATION/*", null)]
    [InlineData("*/*", null)]
    [InlineData("application/json;q=0.1", null)]
    [InlineData("text/html, application/json;q=0.5", null)]
    [InlineData("text/html", 406)]
    [InlineData("application/json;q=0", 406)]
    [InlineData("application/json;q=0, */*;q=1", 406)]
    [InlineData("application/*;q=0, */*;q=1", 406)]
    [InlineData("application/json;q=1, application/*;q=0", null)]
    [InlineData("application/json;q=1, */*;q=0", null)]
    [InlineData("application/json;charset=utf-8", null)]
    [InlineData("application/json;CHARSET=\"UTF-8\"", null)]
    [InlineData("application/json;charset=iso-8859-1", 406)]
    [InlineData("application/json;profile=unknown", 406)]
    [InlineData("application/json;charset=utf-8;q=0, application/json;q=1", 406)]
    [InlineData("application/json;charset=iso-8859-1;q=0, application/json;q=1", null)]
    [InlineData("invalid-media-type", 400)]
    public void Accept_UsesCaseInsensitiveMatchingAndMostSpecificQuality(string? accept, int? expected)
    {
        var context = new DefaultHttpContext();
        if (accept is not null) context.Request.Headers.Accept = accept;

        Assert.Equal(expected, StrictJsonBody.AcceptedRepresentation(context));
    }
}
