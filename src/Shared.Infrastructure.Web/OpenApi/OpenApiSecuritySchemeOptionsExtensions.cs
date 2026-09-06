using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace BitCode.Framework.Shared.Infrastructure.Web.OpenApi;

/// <summary>
/// F1-28: describe en el contrato OpenAPI el esquema de seguridad JWT Bearer que
/// <c>Shared.Infrastructure.Security</c> ofrece (<c>JwtTokenGenerator</c>) -- un proyecto consumidor
/// llama <see cref="AddJwtBearerSecurityScheme"/> SOLO si protege realmente al menos un endpoint con
/// <c>RequireAuthorization(...)</c>/<c>[Authorize]</c>. No es una llamada automática de
/// <c>AddSharedOpenApiForApiVersion</c>: describir un esquema de seguridad que el servidor no exige en
/// ningún endpoint sería un contrato engañoso (ver <c>docs/guia-openapi.md</c>, sección de seguridad,
/// y la nota explícita en <c>samples/Sample.Api</c> de por qué esa muestra NO lo llama -- hoy no
/// protege ningún endpoint con autenticación real).
/// </summary>
public static class OpenApiSecuritySchemeOptionsExtensions
{
    private const string JwtBearerSchemeId = "Bearer";

    /// <summary>
    /// Agrega el esquema <c>Bearer</c> (HTTP, JWT) a <c>components.securitySchemes</c> del documento, y
    /// marca como requerido ese esquema (<c>security</c> a nivel de operación) únicamente en las
    /// operaciones cuyo endpoint tiene metadata de autorización (<see cref="IAuthorizeData"/>) sin
    /// <see cref="IAllowAnonymous"/> -- el resto de las operaciones del documento no se modifica.
    /// </summary>
    public static OpenApiOptions AddJwtBearerSecurityScheme(this OpenApiOptions options)
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            var components = document.Components ?? new OpenApiComponents();
            var securitySchemes = components.SecuritySchemes ?? new Dictionary<string, IOpenApiSecurityScheme>();
            securitySchemes[JwtBearerSchemeId] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Token JWT emitido por JwtTokenGenerator (Shared.Infrastructure.Security).",
            };
            components.SecuritySchemes = securitySchemes;
            document.Components = components;

            return Task.CompletedTask;
        });

        options.AddOperationTransformer((operation, context, _) =>
        {
            var endpointMetadata = context.Description.ActionDescriptor.EndpointMetadata;
            var requiresAuthentication = endpointMetadata.OfType<IAuthorizeData>().Any()
                && !endpointMetadata.OfType<IAllowAnonymous>().Any();

            if (requiresAuthentication)
            {
                var document = context.Document ?? throw new InvalidOperationException(
                    "El OpenApiOperationTransformerContext no tiene un OpenApiDocument asociado.");

                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(JwtBearerSchemeId, document)] = [],
                });
            }

            return Task.CompletedTask;
        });

        return options;
    }
}
