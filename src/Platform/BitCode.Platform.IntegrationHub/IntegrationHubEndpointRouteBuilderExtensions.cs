using Asp.Versioning;
using BitCode.Framework.Platform.IntegrationHub.Conectores;
using BitCode.Framework.Platform.IntegrationHub.Solicitudes;
using BitCode.Framework.Shared.Infrastructure.Web.Results;
using BitCode.Framework.Shared.Kernel;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BitCode.Framework.Platform.IntegrationHub;

/// <summary>Mapea los endpoints HTTP del módulo Integration Hub bajo <c>/api/v1/integrationhub/...</c>.</summary>
public static class IntegrationHubEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapIntegrationHubEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var apiVersionSet = endpoints.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1))
            .ReportApiVersions()
            .Build();

        var group = endpoints.MapGroup("/api/v{version:apiVersion}/integrationhub")
            .WithApiVersionSet(apiVersionSet)
            .HasApiVersion(new ApiVersion(1));

        MapConectores(group);
        MapSolicitudes(group);

        return endpoints;
    }

    private static void MapConectores(RouteGroupBuilder group)
    {
        var conectores = group.MapGroup("/conectores");

        conectores.MapPost("/", async (CrearConectorRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CrearConectorCommand(
                    request.Codigo, request.Nombre, request.BaseUrl, request.Metodo, request.TipoAutenticacion,
                    request.SecretKey, request.ApiKeyHeaderName,
                    request.Mappings.Select(m => new FieldMappingDto(m.CampoOrigen, m.CampoDestino)).ToList()),
                ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/integrationhub/conectores/{result.Value}", result.Value)
                : result.ToProblemDetails();
        })
            .RequireAuthorization(IntegrationHubPermissions.ConectoresAdministrar)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        conectores.MapPost("/{id:guid}/activar", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ActivarConectorCommand(id), ct);
            return result.IsSuccess ? Results.Ok() : result.ToProblemDetails();
        })
            .RequireAuthorization(IntegrationHubPermissions.ConectoresAdministrar)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        conectores.MapPost("/{id:guid}/desactivar", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new DesactivarConectorCommand(id), ct);
            return result.IsSuccess ? Results.Ok() : result.ToProblemDetails();
        })
            .RequireAuthorization(IntegrationHubPermissions.ConectoresAdministrar)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        conectores.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ObtenerConectorQuery(id), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(IntegrationHubPermissions.ConectoresVer)
            .Produces<ConectorResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        conectores.MapGet("/", async (ISender sender, CancellationToken ct, int page = 1, int pageSize = 20) =>
        {
            var result = await sender.Send(new ListarConectoresQuery(page, pageSize), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(IntegrationHubPermissions.ConectoresVer)
            .Produces<PagedResult<ConectorResumenResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);
    }

    private static void MapSolicitudes(RouteGroupBuilder group)
    {
        var solicitudes = group.MapGroup("/solicitudes");

        solicitudes.MapPost("/enviar", async (EnviarSolicitudRequestDto request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new EnviarSolicitudIntegracionCommand(request.ConectorCodigo, request.PayloadJson), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(IntegrationHubPermissions.SolicitudesEnviar)
            .Produces<Guid>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        solicitudes.MapGet("/", async (
            ISender sender, CancellationToken ct, Guid? connectorId = null, IntegrationRequestEstado? estado = null,
            int page = 1, int pageSize = 20) =>
        {
            var result = await sender.Send(new ListarSolicitudesQuery(connectorId, estado, page, pageSize), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(IntegrationHubPermissions.SolicitudesVer)
            .Produces<PagedResult<IntegrationRequestResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        solicitudes.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ObtenerSolicitudQuery(id), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(IntegrationHubPermissions.SolicitudesVer)
            .Produces<IntegrationRequestResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        solicitudes.MapGet("/{id:guid}/logs", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListarLogsDeSolicitudQuery(id), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(IntegrationHubPermissions.SolicitudesVer)
            .Produces<IReadOnlyList<IntegrationRequestLogResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private sealed record FieldMappingRequestDto(string CampoOrigen, string CampoDestino);

    private sealed record CrearConectorRequest(
        string Codigo,
        string Nombre,
        string BaseUrl,
        MetodoHttpConector Metodo,
        TipoAutenticacionConector TipoAutenticacion,
        string? SecretKey,
        string? ApiKeyHeaderName,
        IReadOnlyList<FieldMappingRequestDto> Mappings);

    private sealed record EnviarSolicitudRequestDto(string ConectorCodigo, string PayloadJson);
}
