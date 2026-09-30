using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Mrp.Api;

/// <summary>Declares the JWT bearer scheme in the OpenAPI document so generated clients know how to authenticate.</summary>
public sealed class BearerSecurityTransformer : IOpenApiDocumentTransformer
{
    /// <inheritdoc />
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info.Title = "MRP SaaS API";
        document.Info.Version = "v1";
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Access token from POST /api/v1/auth/login",
        };
        return Task.CompletedTask;
    }
}
