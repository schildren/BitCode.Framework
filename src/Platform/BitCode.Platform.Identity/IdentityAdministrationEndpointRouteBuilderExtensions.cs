using Asp.Versioning;
using Asp.Versioning.Builder;
using BitCode.Framework.Platform.Identity.Roles;
using BitCode.Framework.Platform.Identity.Sessions;
using BitCode.Framework.Platform.Identity.Users;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using BitCode.Framework.Shared.Infrastructure.Web.Results;
using BitCode.Framework.Shared.Kernel;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BitCode.Framework.Platform.Identity;

/// <summary>
/// Mapea los endpoints HTTP del módulo Identity Administration bajo <c>/api/v1/identidad/...</c>. Un
/// host consumidor llama a este método desde su propio módulo (<c>IWebFrameworkModule</c> con
/// <c>[DependsOn(typeof(InfrastructureModule))]</c>, en el ensamblado del host -- este método no
/// puede declarar esa dependencia por sí mismo porque <c>InfrastructureModule</c> vive en el host, no
/// en esta librería). Los comandos/queries/DTOs que estos endpoints usan son <c>internal</c> a este
/// ensamblado: el host nunca los referencia directamente, solo este único punto de entrada público.
/// Ver <c>docs/guia-identity-administration.md</c>.
/// </summary>
public static class IdentityAdministrationEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapIdentityAdministrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var apiVersionSet = endpoints.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1))
            .ReportApiVersions()
            .Build();

        var group = endpoints.MapGroup("/api/v{version:apiVersion}/identidad")
            .WithApiVersionSet(apiVersionSet)
            .HasApiVersion(new ApiVersion(1));

        MapUsuariosEndpoints(group);
        MapRolesEndpoints(group);
        MapSesionesEndpoints(group);

        return endpoints;
    }

    private static void MapUsuariosEndpoints(RouteGroupBuilder group)
    {
        var usuarios = group.MapGroup("/usuarios");

        usuarios.MapPost("/", async (CrearUsuarioCommand command, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/identidad/usuarios/{result.Value}", result.Value)
                : result.ToProblemDetails();
        })
            .RequireAuthorization(IdentityAdministrationPermissions.UsuariosCrear)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        usuarios.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ObtenerUsuarioQuery(id), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(IdentityAdministrationPermissions.UsuariosVer)
            .Produces<UsuarioResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        usuarios.MapGet("/", async (ISender sender, CancellationToken ct, int page = 1, int pageSize = 20) =>
        {
            var result = await sender.Send(new ListarUsuariosQuery(page, pageSize), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(IdentityAdministrationPermissions.UsuariosVer)
            .Produces<PagedResult<UsuarioResponse>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        usuarios.MapPost("/{id:guid}/desactivar", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new DesactivarUsuarioCommand(id), ct);
            return result.IsSuccess ? Results.Ok() : result.ToProblemDetails();
        })
            .RequireAuthorization(IdentityAdministrationPermissions.UsuariosDesactivar)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        usuarios.MapPost("/{id:guid}/roles", async (Guid id, AsignarRolAUsuarioRequest body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new AsignarRolAUsuarioCommand(id, body.NombreRol), ct);
            return result.IsSuccess ? Results.Ok() : result.ToProblemDetails();
        })
            .RequireAuthorization(IdentityAdministrationPermissions.UsuariosRolesAsignar)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        usuarios.MapGet("/{id:guid}/sesiones", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListarSesionesQuery(id), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(IdentityAdministrationPermissions.SesionesVer)
            .Produces<IReadOnlyList<SesionResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapRolesEndpoints(RouteGroupBuilder group)
    {
        var roles = group.MapGroup("/roles");

        roles.MapPost("/", async (CrearRolCommand command, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/identidad/roles/{result.Value}", result.Value)
                : result.ToProblemDetails();
        })
            .RequireAuthorization(IdentityAdministrationPermissions.RolesCrear)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        roles.MapGet("/", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListarRolesQuery(), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(IdentityAdministrationPermissions.RolesVer)
            .Produces<IReadOnlyList<RolResponse>>(StatusCodes.Status200OK);

        roles.MapPost("/{nombreRol}/permisos", async (string nombreRol, AsignarPermisoRequest body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new AsignarPermisoARolCommand(nombreRol, body.Permiso), ct);
            return result.IsSuccess ? Results.Ok() : result.ToProblemDetails();
        })
            .RequireAuthorization(IdentityAdministrationPermissions.RolesPermisosAdministrar)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        roles.MapDelete("/{nombreRol}/permisos/{permiso}", async (string nombreRol, string permiso, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new QuitarPermisoDeRolCommand(nombreRol, permiso), ct);
            return result.IsSuccess ? Results.Ok() : result.ToProblemDetails();
        })
            .RequireAuthorization(IdentityAdministrationPermissions.RolesPermisosAdministrar)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);
    }

    private static void MapSesionesEndpoints(RouteGroupBuilder group)
    {
        var sesiones = group.MapGroup("/sesiones");

        sesiones.MapPost("/{id:guid}/revocar", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new RevocarSesionCommand(id), ct);
            return result.IsSuccess ? Results.Ok() : result.ToProblemDetails();
        })
            .RequireAuthorization(IdentityAdministrationPermissions.SesionesRevocar)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);
    }
}

/// <summary>Cuerpo del POST de asignación de rol a usuario -- separado del comando (el binding de
/// Minimal API necesita un tipo propio para el body, distinto del comando que también carga el
/// <c>UserId</c> tomado de la ruta).</summary>
internal sealed record AsignarRolAUsuarioRequest(string NombreRol);

/// <summary>Cuerpo del POST de asignación de permiso a rol.</summary>
internal sealed record AsignarPermisoRequest(string Permiso);
