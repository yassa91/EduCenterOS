using EduCenterOS.Modules.IdentityAccess;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace EduCenterOS.Api.Configuration;

internal static class ApiOpenApi
{
    internal static void Configure(OpenApiOptions options)
    {
        options.ConfigureIdentityAccessOpenApi();
        options.AddDocumentTransformer((document, context, cancellation) =>
        {
            document.Info.Title = "EduCenterOS API";
            document.Info.Version = "v1";
            document.Info.Description = "Local Development/Testing account registration. Anonymous security commands; registration grants no login or institution access.";

            return Task.CompletedTask;
        });
        options.AddSchemaTransformer((schema, context, cancellation) =>
        {
            if (context.JsonTypeInfo.Type != typeof(ProblemDetails)) return Task.CompletedTask;

            schema.Type = JsonSchemaType.Object;
            schema.AdditionalPropertiesAllowed = false;
            schema.Required = new HashSet<string>(["type", "title", "status", "detail", "code", "correlationId"]);
            schema.Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["type"] = Text("Stable urn:educenteros:problem category."),
                ["title"] = Text("Safe category title."),
                ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
                ["detail"] = Text("Safe explanation; never includes submitted values."),
                ["code"] = Text("Stable registered error code."),
                ["correlationId"] = Text("Matches the X-Correlation-Id response header."),
                ["traceId"] = Text("Optional active trace identifier."),
                ["errorsTruncated"] = new OpenApiSchema { Type = JsonSchemaType.Boolean, Description = "Present with field issues; at most 50 unique location/code issues." },
                ["errors"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    Description = "Optional validation dictionary keyed by body field location; safe code/description arrays.",
                    AdditionalPropertiesAllowed = true,
                    AdditionalProperties = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Array,
                        Items = new OpenApiSchema
                        {
                            Type = JsonSchemaType.Object,
                            AdditionalPropertiesAllowed = false,
                            Required = new HashSet<string>(["code", "description"]),
                            Properties = new Dictionary<string, IOpenApiSchema> { ["code"] = Text("Stable field code."), ["description"] = Text("Safe field description.") }
                        }
                    }
                }
            };

            return Task.CompletedTask;
        });
    }

    private static OpenApiSchema Text(string description) => new() { Type = JsonSchemaType.String, Description = description };
}
