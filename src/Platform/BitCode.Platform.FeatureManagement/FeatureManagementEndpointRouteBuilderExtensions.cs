using Asp.Versioning;
using Asp.Versioning.Builder;
using BitCode.Framework.Platform.FeatureManagement.Evaluacion;
using BitCode.Framework.Platform.FeatureManagement.Flags;
using BitCode.Framework.Platform.FeatureManagement.Rollouts;
using BitCode.Framework.Platform.FeatureManagement.Segmentos;
using BitCode.Framework.Shared.Infrastructure.Web.Results;
using BitCode.Framework.Shared.Kernel;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BitCode.Framework.Platform.FeatureManagement;

/// <summary>
/// Mapea los endpoints HTTP del módulo Feature Management bajo <c>/api/v1/feature-flags/...</c>,
/// <c>/api/v1/segmentos/...</c> y <c>/api/v1/rollouts/...</c>. Un host consumidor llama a este método
/// desde su propio módulo (<c>IWebFrameworkModule</c> con <c>[DependsOn(typeof(InfrastructureModule))]</c>,
/// en el ensamblado del host). Los comandos/queries/DTOs que estos endpoints usan son <c>internal</c> a
/// este ensamblado -- el host nunca los referencia directamente, solo este único punto de entrada
/// público. Ver <c>docs/guia-feature-management.md</c>.
/// </summary>
public static class FeatureManagementEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapFeatureManagementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var apiVersionSet = endpoints.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1))
            .ReportApiVersions()
            .Build();

        MapFeatureFlagsEndpoints(endpoints, apiVersionSet);
        MapSegmentosEndpoints(endpoints, apiVersionSet);
        MapRolloutsEndpoints(endpoints, apiVersionSet);

        return endpoints;
    }

    private static void MapFeatureFlagsEndpoints(IEndpointRouteBuilder endpoints, ApiVersionSet apiVersionSet)
    {
        var group = endpoints.MapGroup("/api/v{version:apiVersion}/feature-flags")
            .WithApiVersionSet(apiVersionSet)
            .HasApiVersion(new ApiVersion(1));

        group.MapPost("/", async (CrearFeatureFlagRequest body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CrearFeatureFlagCommand(body.Nombre, body.Descripcion), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/feature-flags/{result.Value}", result.Value)
                : result.ToProblemDetails();
        })
            .RequireAuthorization(FeatureManagementPermissions.FlagsCrear)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ObtenerFeatureFlagQuery(id), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(FeatureManagementPermissions.FlagsVer)
            .Produces<FeatureFlagResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", async (ISender sender, CancellationToken ct, int page = 1, int pageSize = 20) =>
        {
            var result = await sender.Send(new ListarFeatureFlagsQuery(page, pageSize), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(FeatureManagementPermissions.FlagsVer)
            .Produces<PagedResult<FeatureFlagResponse>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{id:guid}/activar", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ActivarFeatureFlagCommand(id), ct);
            return result.IsSuccess ? Results.Ok() : result.ToProblemDetails();
        })
            .RequireAuthorization(FeatureManagementPermissions.FlagsActivar)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/desactivar", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new DesactivarFeatureFlagCommand(id), ct);
            return result.IsSuccess ? Results.Ok() : result.ToProblemDetails();
        })
            .RequireAuthorization(FeatureManagementPermissions.FlagsDesactivar)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // El endpoint de evaluación REAL exigido por el Plan Maestro: dado un nombre de flag y un
        // contexto (tenant/usuario), ¿está activo? Permiso separado (FlagsEvaluar), ver
        // FeatureManagementPermissions para el razonamiento.
        group.MapGet("/{nombre}/evaluar", async (string nombre, Guid tenantId, string? userId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new EvaluarFeatureFlagQuery(nombre, tenantId, userId), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(FeatureManagementPermissions.FlagsEvaluar)
            .Produces<FeatureFlagEvaluationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);
    }

    private static void MapSegmentosEndpoints(IEndpointRouteBuilder endpoints, ApiVersionSet apiVersionSet)
    {
        var group = endpoints.MapGroup("/api/v{version:apiVersion}/segmentos")
            .WithApiVersionSet(apiVersionSet)
            .HasApiVersion(new ApiVersion(1));

        group.MapPost("/", async (CrearSegmentoRequest body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CrearSegmentoCommand(body.Nombre, body.Tipo, body.TenantIdCriterio, body.Porcentaje), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/segmentos/{result.Value}", result.Value)
                : result.ToProblemDetails();
        })
            .RequireAuthorization(FeatureManagementPermissions.SegmentosCrear)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ObtenerSegmentoQuery(id), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(FeatureManagementPermissions.SegmentosVer)
            .Produces<SegmentoResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", async (ISender sender, CancellationToken ct, int page = 1, int pageSize = 20) =>
        {
            var result = await sender.Send(new ListarSegmentosQuery(page, pageSize), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(FeatureManagementPermissions.SegmentosVer)
            .Produces<PagedResult<SegmentoResponse>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);
    }

    private static void MapRolloutsEndpoints(IEndpointRouteBuilder endpoints, ApiVersionSet apiVersionSet)
    {
        var group = endpoints.MapGroup("/api/v{version:apiVersion}/rollouts")
            .WithApiVersionSet(apiVersionSet)
            .HasApiVersion(new ApiVersion(1));

        group.MapPost("/", async (CrearRolloutRequest body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CrearRolloutCommand(body.FeatureFlagId, body.SegmentoId), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/rollouts/{result.Value}", result.Value)
                : result.ToProblemDetails();
        })
            .RequireAuthorization(FeatureManagementPermissions.RolloutsCrear)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/por-flag/{featureFlagId:guid}", async (Guid featureFlagId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListarRolloutsDeFlagQuery(featureFlagId), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(FeatureManagementPermissions.RolloutsVer)
            .Produces<IReadOnlyList<RolloutResponse>>(StatusCodes.Status200OK);
    }
}

/// <summary>Cuerpo del POST de alta de flag.</summary>
internal sealed record CrearFeatureFlagRequest(string Nombre, string? Descripcion);

/// <summary>Cuerpo del POST de alta de segmento -- <see cref="TenantIdCriterio"/>/<see cref="Porcentaje"/>
/// son mutuamente relevantes según <see cref="Tipo"/> (ver <c>CrearSegmentoCommandValidator</c>).</summary>
internal sealed record CrearSegmentoRequest(string Nombre, SegmentoTipo Tipo, Guid? TenantIdCriterio, int? Porcentaje);

/// <summary>Cuerpo del POST de alta de rollout (asociación flag-segmento).</summary>
internal sealed record CrearRolloutRequest(Guid FeatureFlagId, Guid SegmentoId);
