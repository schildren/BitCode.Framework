using Asp.Versioning;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Web.OpenApi;

/// <summary>
/// F1-28: generación del contrato OpenAPI usando el soporte NATIVO de .NET 10
/// (<c>Microsoft.AspNetCore.OpenApi</c>, <c>AddOpenApi</c>/<c>MapOpenApi</c>) en vez de
/// Swashbuckle/NSwag -- cubre generación, transformers (examples, seguridad) y filtrado por documento
/// sin una dependencia externa adicional. Ver <c>docs/guia-openapi.md</c>.
/// </summary>
public static class OpenApiServiceCollectionExtensions
{
    /// <summary>
    /// Registra un documento OpenAPI SEPARADO para una única versión mayor de API HTTP (F1-27):
    /// <c>/openapi/v{majorVersion}.json</c> (ruta que mapea <c>app.MapOpenApi()</c>, un único llamado
    /// para todos los documentos registrados). Un proyecto con N versiones coexistentes llama este
    /// método una vez por versión -- nunca se mezclan los contratos de dos versiones en un mismo
    /// documento, porque eso oscurecería justo lo que F1-27 garantiza (contratos de v1/v2 que
    /// coexisten sin pisarse: ver <c>samples/Sample.Api/Productos/ProductosModule.cs</c>).
    /// <para>
    /// El filtro de inclusión (<see cref="OpenApiOptions.ShouldInclude"/>) reconoce la metadata que
    /// agrega <c>Asp.Versioning.Http</c> a cada endpoint mapeado con <c>.HasApiVersion(...)</c>/
    /// <c>.MapToApiVersion(...)</c> (<see cref="ApiVersionMetadata"/>): un endpoint SIN esa metadata
    /// (por ejemplo, <c>/health/live</c>/<c>/health/ready</c>, que no pasan por ningún
    /// <c>ApiVersionSet</c>) o marcado explícitamente <c>ApiVersionNeutral</c> aparece en TODOS los
    /// documentos por versión, porque no es parte de un contrato de negocio versionado.
    /// </para>
    /// </summary>
    /// <param name="services">Contenedor de servicios del proyecto consumidor.</param>
    /// <param name="majorVersion">
    /// Versión mayor de API HTTP (la misma que <c>new ApiVersion(majorVersion)</c> en el
    /// <c>ApiVersionSet</c> del/los módulo/s que exponen esa versión).
    /// </param>
    /// <param name="configureOptions">
    /// Personalización adicional del documento (título, transformers propios). Se ejecuta DESPUÉS del
    /// <see cref="OpenApiOptions.ShouldInclude"/> ya configurado por este método -- puede reemplazarlo,
    /// así que no debe sobreescribirlo salvo intención explícita de cambiar el filtrado por versión.
    /// </param>
    public static IServiceCollection AddSharedOpenApiForApiVersion(
        this IServiceCollection services,
        int majorVersion,
        Action<OpenApiOptions>? configureOptions = null)
    {
        var documentName = $"v{majorVersion}";

        services.AddOpenApi(documentName, options =>
        {
            options.ShouldInclude = apiDescription => MatchesApiVersionOrIsVersionNeutral(apiDescription, majorVersion);
            configureOptions?.Invoke(options);
        });

        return services;
    }

    private static bool MatchesApiVersionOrIsVersionNeutral(ApiDescription apiDescription, int majorVersion)
    {
        var metadata = apiDescription.ActionDescriptor.EndpointMetadata
            .OfType<ApiVersionMetadata>()
            .FirstOrDefault();

        // Sin metadata de Asp.Versioning (ej. /health/live, /health/ready) o marcado explícitamente
        // version-neutral: no es un endpoint versionado de negocio, se documenta en todos los
        // documentos por versión en vez de quedar fuera de todos ellos.
        if (metadata is null || metadata.IsApiVersionNeutral)
        {
            return true;
        }

        return metadata.IsMappedTo(new ApiVersion(majorVersion));
    }
}
