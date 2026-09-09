using Asp.Versioning;
using Asp.Versioning.Builder;
using BitCode.Framework.Platform.Organization.Areas;
using BitCode.Framework.Platform.Organization.Cargos;
using BitCode.Framework.Platform.Organization.Empresas;
using BitCode.Framework.Platform.Organization.Sucursales;
using BitCode.Framework.Shared.Infrastructure.Web.Results;
using BitCode.Framework.Shared.Kernel;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BitCode.Framework.Platform.Organization;

/// <summary>
/// Mapea los endpoints HTTP del módulo Organization bajo <c>/api/v1/organizacion/...</c>. Un host
/// consumidor llama a este método desde su propio módulo (<c>IWebFrameworkModule</c> con
/// <c>[DependsOn(typeof(InfrastructureModule))]</c>, en el ensamblado del host). Los comandos/queries/
/// DTOs que estos endpoints usan son <c>internal</c> a este ensamblado -- el host nunca los referencia
/// directamente, solo este único punto de entrada público. Ver <c>docs/guia-organization.md</c>.
/// </summary>
public static class OrganizationEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var apiVersionSet = endpoints.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1))
            .ReportApiVersions()
            .Build();

        var group = endpoints.MapGroup("/api/v{version:apiVersion}/organizacion")
            .WithApiVersionSet(apiVersionSet)
            .HasApiVersion(new ApiVersion(1));

        MapEmpresasEndpoints(group);
        MapSucursalesEndpoints(group);
        MapAreasEndpoints(group);

        return endpoints;
    }

    private static void MapEmpresasEndpoints(RouteGroupBuilder group)
    {
        var empresas = group.MapGroup("/empresas");

        empresas.MapPost("/", async (CrearEmpresaCommand command, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/organizacion/empresas/{result.Value}", result.Value)
                : result.ToProblemDetails();
        })
            .RequireAuthorization(OrganizationPermissions.EmpresasCrear)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        empresas.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ObtenerEmpresaQuery(id), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(OrganizationPermissions.EmpresasVer)
            .Produces<EmpresaResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        empresas.MapGet("/", async (ISender sender, CancellationToken ct, int page = 1, int pageSize = 20) =>
        {
            var result = await sender.Send(new ListarEmpresasQuery(page, pageSize), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(OrganizationPermissions.EmpresasVer)
            .Produces<PagedResult<EmpresaResponse>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        empresas.MapPost("/{id:guid}/desactivar", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new DesactivarEmpresaCommand(id), ct);
            return result.IsSuccess ? Results.Ok() : result.ToProblemDetails();
        })
            .RequireAuthorization(OrganizationPermissions.EmpresasDesactivar)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        empresas.MapPost("/{empresaId:guid}/sucursales", async (Guid empresaId, CrearSucursalRequest body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CrearSucursalCommand(empresaId, body.Nombre, body.Direccion), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/organizacion/sucursales/{result.Value}", result.Value)
                : result.ToProblemDetails();
        })
            .RequireAuthorization(OrganizationPermissions.SucursalesCrear)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        empresas.MapGet("/{empresaId:guid}/sucursales", async (Guid empresaId, ISender sender, CancellationToken ct, int page = 1, int pageSize = 20) =>
        {
            var result = await sender.Send(new ListarSucursalesQuery(empresaId, page, pageSize), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(OrganizationPermissions.SucursalesVer)
            .Produces<PagedResult<SucursalResponse>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);
    }

    private static void MapSucursalesEndpoints(RouteGroupBuilder group)
    {
        var sucursales = group.MapGroup("/sucursales");

        sucursales.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ObtenerSucursalQuery(id), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(OrganizationPermissions.SucursalesVer)
            .Produces<SucursalResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        sucursales.MapPost("/{id:guid}/desactivar", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new DesactivarSucursalCommand(id), ct);
            return result.IsSuccess ? Results.Ok() : result.ToProblemDetails();
        })
            .RequireAuthorization(OrganizationPermissions.SucursalesDesactivar)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        sucursales.MapPost("/{sucursalId:guid}/areas", async (Guid sucursalId, CrearAreaRequest body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CrearAreaCommand(sucursalId, body.Nombre, body.ParentAreaId), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/organizacion/areas/{result.Value}", result.Value)
                : result.ToProblemDetails();
        })
            .RequireAuthorization(OrganizationPermissions.AreasCrear)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        sucursales.MapGet("/{sucursalId:guid}/areas", async (Guid sucursalId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListarAreasQuery(sucursalId), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(OrganizationPermissions.AreasVer)
            .Produces<IReadOnlyList<AreaResponse>>(StatusCodes.Status200OK);
    }

    private static void MapAreasEndpoints(RouteGroupBuilder group)
    {
        var areas = group.MapGroup("/areas");

        areas.MapPost("/{areaId:guid}/cargos", async (Guid areaId, CrearCargoRequest body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CrearCargoCommand(areaId, body.Nombre), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/organizacion/cargos/{result.Value}", result.Value)
                : result.ToProblemDetails();
        })
            .RequireAuthorization(OrganizationPermissions.CargosCrear)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        areas.MapGet("/{areaId:guid}/cargos", async (Guid areaId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListarCargosQuery(areaId), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(OrganizationPermissions.CargosVer)
            .Produces<IReadOnlyList<CargoResponse>>(StatusCodes.Status200OK);
    }

}

/// <summary>Cuerpo del POST de alta de sucursal bajo una empresa -- separado del comando porque el
/// binding de Minimal API necesita un tipo propio para el body, distinto del comando que también carga
/// el <c>EmpresaId</c> tomado de la ruta.</summary>
internal sealed record CrearSucursalRequest(string Nombre, string? Direccion);

/// <summary>Cuerpo del POST de alta de área bajo una sucursal.</summary>
internal sealed record CrearAreaRequest(string Nombre, Guid? ParentAreaId);

/// <summary>Cuerpo del POST de alta de cargo bajo un área.</summary>
internal sealed record CrearCargoRequest(string Nombre);
