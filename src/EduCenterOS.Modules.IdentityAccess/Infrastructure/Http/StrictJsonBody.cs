using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Http;

internal static class StrictJsonBody
{
    private const int MaximumBytes = 16384;
    private static readonly MediaTypeHeaderValue JsonRepresentation = MediaTypeHeaderValue.Parse("application/json; charset=utf-8").CopyAsReadOnly();
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

        if (AcceptedRepresentation(context) is { } acceptStatus) return transportError(acceptStatus);

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

    internal static int? AcceptedRepresentation(HttpContext context)
    {
        try
        {
            var accepted = MediaTypeHeaderValue.ParseStrictList(context.Request.Headers.Accept);

            if (accepted.Count == 0) return null;

            // Charset tokens are case-insensitive and may be quoted.
            foreach (var media in accepted)
                if (string.Equals(media.Charset.Value?.Trim('"'), "utf-8", StringComparison.OrdinalIgnoreCase))
                    media.Charset = "utf-8";

            // A specific q=0 excludes JSON even if a broader wildcard permits it.
            var match = accepted.Where(media => JsonRepresentation.IsSubsetOf(media))
                .OrderByDescending(media => media.MatchesAllTypes ? 0 : media.MatchesAllSubTypes ? 1 : 2)
                .ThenByDescending(media => media.Parameters.Count(parameter => !parameter.Name.Equals("q", StringComparison.OrdinalIgnoreCase)))
                .ThenByDescending(media => media.Quality ?? 1)
                .FirstOrDefault();

            if (match is null || match.Quality == 0) return 406;
        }
        catch (FormatException)
        {
            return 400;
        }

        return null;
    }

    internal static bool RouteId(HttpContext context, out Guid id)
    {
        id = Guid.Empty;
        var text = context.Request.RouteValues["challengeId"]?.ToString();

        return text is { Length: 36 } && Guid.TryParseExact(text, "D", out id) && id != Guid.Empty;
    }
}
