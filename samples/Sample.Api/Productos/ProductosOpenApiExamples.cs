using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Sample.Api.Productos;

/// <summary>
/// F1-28: agrega un ejemplo real de request/response a los dos endpoints principales de Productos
/// (<c>POST /api/v1/productos</c>, <c>GET /api/v2/productos/{id}</c>) usando el mecanismo nativo de
/// .NET 10 (<see cref="IOpenApiOperationTransformer"/>). Vive en <c>samples/Sample.Api</c> y no en
/// <c>Shared.Infrastructure.Web</c> a propósito: un ejemplo de negocio concreto (nombre/precio de un
/// producto) es contenido específico de este feature, no algo que el framework pueda generalizar.
/// Registrado por <see cref="ProductosModule.ConfigureServices"/> vía
/// <c>services.Configure&lt;OpenApiOptions&gt;("v{n}", ...)</c> -- no en <c>InfrastructureModule</c>,
/// que solo conoce documentos genéricos por versión (<c>AddSharedOpenApiForApiVersion</c>), nunca un
/// tipo concreto de un feature.
/// </summary>
internal sealed class ProductosExampleOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var description = context.Description;
        var apiVersionMetadata = description.ActionDescriptor.EndpointMetadata
            .OfType<ApiVersionMetadata>()
            .FirstOrDefault();
        var tieneParametroId = description.ParameterDescriptions.Any(p => p.Name == "id");

        if (HttpMethods.IsPost(description.HttpMethod ?? string.Empty))
        {
            AgregarEjemploCrearProducto(operation);
        }
        else if (HttpMethods.IsGet(description.HttpMethod ?? string.Empty)
            && tieneParametroId
            && apiVersionMetadata is not null
            && apiVersionMetadata.IsMappedTo(new ApiVersion(2)))
        {
            AgregarEjemploObtenerProductoV2(operation);
        }

        return Task.CompletedTask;
    }

    private static void AgregarEjemploCrearProducto(OpenApiOperation operation)
    {
        if (operation.RequestBody is OpenApiRequestBody requestBody
            && requestBody.Content?.TryGetValue("application/json", out var requestMediaType) == true)
        {
            requestMediaType!.Example = ToJsonNode(new CrearProductoCommand("Teclado mecánico", 89.90m));
        }

        if (operation.Responses?.TryGetValue("201", out var createdResponse) == true
            && createdResponse is OpenApiResponse concreteCreatedResponse
            && concreteCreatedResponse.Content?.TryGetValue("application/json", out var createdMediaType) == true)
        {
            createdMediaType!.Example = ToJsonNode(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        }
    }

    private static void AgregarEjemploObtenerProductoV2(OpenApiOperation operation)
    {
        if (operation.Responses?.TryGetValue("200", out var okResponse) == true
            && okResponse is OpenApiResponse concreteOkResponse
            && concreteOkResponse.Content?.TryGetValue("application/json", out var okMediaType) == true)
        {
            okMediaType!.Example = ToJsonNode(new ProductoResponseV2(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "Teclado mecánico",
                89.90m,
                new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc)));
        }
    }

    private static System.Text.Json.Nodes.JsonNode ToJsonNode<T>(T value) =>
        JsonSerializer.SerializeToNode(value)!;
}
