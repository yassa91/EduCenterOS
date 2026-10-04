using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;

internal static class StrictJsonBody
{
    private const int MaximumBytes = 16384;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };

    internal static async Task<IResult> ReadAsync<T>(HttpContext context, Func<T, Task<IResult>> action, Func<int, IResult> transportError)
    {
        if (context.Request.QueryString.HasValue) return transportError(400);

        if (
            !MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var contentType) ||
            !string.Equals(contentType.MediaType.Value, "application/json", StringComparison.OrdinalIgnoreCase) ||
            (contentType.Charset.HasValue && !string.Equals(contentType.Charset.Value?.Trim('"'), "utf-8", StringComparison.OrdinalIgnoreCase)) ||
            (context.Request.Headers.ContentEncoding.Count > 0 && context.Request.Headers.ContentEncoding != "identity")
        )
            return transportError(415);

        try
        {
            var accepted = context.Request.GetTypedHeaders().Accept;

            if (
                accepted is { Count: > 0 } &&
                !accepted.Any(media => media.Quality != 0 && media.MediaType.Value is "application/json" or "application/*" or "*/*")
            )
                return transportError(406);
        }
        catch (FormatException)
        {
            return transportError(400);
        }

        if (context.Request.ContentLength > MaximumBytes) return transportError(413);

        using var bytes = new MemoryStream();
        var buffer = new byte[4096];

        while (true)
        {
            var count = await context.Request.Body.ReadAsync(buffer, context.RequestAborted);

            if (count == 0) break;

            if (bytes.Length + count > MaximumBytes) return transportError(413);

            bytes.Write(buffer, 0, count);
        }

        T? request;

        try
        {
            using var document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });

            if (document.RootElement.ValueKind != JsonValueKind.Object) return transportError(400);

            var fields = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();

            if (fields.Distinct(StringComparer.OrdinalIgnoreCase).Count() != fields.Length) return transportError(400);

            request = document.RootElement.Deserialize<T>(Options);

            if (request is null) return transportError(400);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            return transportError(400);
        }

        return await action(request);
    }

    internal static bool RouteId(HttpContext context, out Guid id)
    {
        id = Guid.Empty;
        var text = context.Request.RouteValues["challengeId"]?.ToString();

        return text is { Length: 36 } && Guid.TryParseExact(text, "D", out id) && id != Guid.Empty;
    }
}
