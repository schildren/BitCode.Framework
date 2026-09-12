using Asp.Versioning;
using Asp.Versioning.Builder;
using BitCode.Framework.Platform.Workflow.Definiciones;
using BitCode.Framework.Platform.Workflow.Instancias;
using BitCode.Framework.Shared.Infrastructure.Web.Results;
using BitCode.Framework.Shared.Kernel;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BitCode.Framework.Platform.Workflow;

/// <summary>
/// Mapea los endpoints HTTP del módulo Workflow bajo <c>/api/v1/workflows/...</c>. Un host consumidor
/// llama a este método desde su propio módulo (<c>IWebFrameworkModule</c> con
/// <c>[DependsOn(typeof(InfrastructureModule))]</c>, en el ensamblado del host). Los comandos/queries/DTOs
/// que estos endpoints usan son <c>internal</c> a este ensamblado -- el host nunca los referencia
/// directamente, solo este único punto de entrada público. Ver <c>docs/guia-workflow.md</c>.
/// </summary>
public static class WorkflowEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapWorkflowEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var apiVersionSet = endpoints.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1))
            .ReportApiVersions()
            .Build();

        MapDefinicionesEndpoints(endpoints, apiVersionSet);
        MapInstanciasEndpoints(endpoints, apiVersionSet);
        MapTareasEndpoints(endpoints, apiVersionSet);

        return endpoints;
    }

    private static void MapDefinicionesEndpoints(IEndpointRouteBuilder endpoints, ApiVersionSet apiVersionSet)
    {
        var group = endpoints.MapGroup("/api/v{version:apiVersion}/workflows")
            .WithApiVersionSet(apiVersionSet)
            .HasApiVersion(new ApiVersion(1));

        group.MapPost("/", async (CrearWorkflowDefinitionRequest body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CrearWorkflowDefinitionCommand(body.Codigo, body.Nombre, body.Descripcion), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/workflows/{result.Value}", result.Value)
                : result.ToProblemDetails();
        })
            .RequireAuthorization(WorkflowPermissions.DefinicionesCrear)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ObtenerWorkflowDefinitionQuery(id), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(WorkflowPermissions.DefinicionesVer)
            .Produces<WorkflowDefinitionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", async (ISender sender, CancellationToken ct, int page = 1, int pageSize = 20) =>
        {
            var result = await sender.Send(new ListarWorkflowDefinitionsQuery(page, pageSize), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(WorkflowPermissions.DefinicionesVer)
            .Produces<PagedResult<WorkflowDefinitionResponse>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{workflowDefinitionId:guid}/versiones", async (
            Guid workflowDefinitionId, CrearWorkflowVersionRequest body, ISender sender, CancellationToken ct) =>
        {
            var estados = body.Estados
                .Select(e => new WorkflowStateInput(
                    e.Codigo, e.Nombre, e.EsInicial, e.EsFinal, e.RequiereTarea, e.TituloTarea,
                    e.AsignadoPorDefectoUserId, e.SlaMinutos, e.EscalarAUserId))
                .ToList();
            var transiciones = body.Transiciones
                .Select(t => new WorkflowTransitionInput(t.DesdeCodigo, t.HaciaCodigo, t.Accion, t.ReglaExpresion, t.Orden))
                .ToList();

            var result = await sender.Send(new CrearWorkflowVersionCommand(workflowDefinitionId, estados, transiciones), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/workflows/versiones/{result.Value}", result.Value)
                : result.ToProblemDetails();
        })
            .RequireAuthorization(WorkflowPermissions.VersionesCrear)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        var versiones = group.MapGroup("/versiones");

        versiones.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ObtenerWorkflowVersionQuery(id), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(WorkflowPermissions.DefinicionesVer)
            .Produces<WorkflowVersionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        versiones.MapPost("/{id:guid}/publicar", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new PublicarWorkflowVersionCommand(id), ct);
            return result.IsSuccess ? Results.Ok() : result.ToProblemDetails();
        })
            .RequireAuthorization(WorkflowPermissions.VersionesPublicar)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);
    }

    private static void MapInstanciasEndpoints(IEndpointRouteBuilder endpoints, ApiVersionSet apiVersionSet)
    {
        var group = endpoints.MapGroup("/api/v{version:apiVersion}/workflows/instancias")
            .WithApiVersionSet(apiVersionSet)
            .HasApiVersion(new ApiVersion(1));

        group.MapPost("/", async (IniciarInstanciaRequest body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new IniciarInstanciaCommand(body.WorkflowDefinitionId, body.Variables), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/workflows/instancias/{result.Value}", result.Value)
                : result.ToProblemDetails();
        })
            .RequireAuthorization(WorkflowPermissions.InstanciasIniciar)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ObtenerInstanciaQuery(id), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(WorkflowPermissions.InstanciasVer)
            .Produces<WorkflowInstanceResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/historial", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ObtenerHistorialInstanciaQuery(id), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(WorkflowPermissions.InstanciasVer)
            .Produces<IReadOnlyList<WorkflowHistorialResponse>>(StatusCodes.Status200OK);
    }

    private static void MapTareasEndpoints(IEndpointRouteBuilder endpoints, ApiVersionSet apiVersionSet)
    {
        var group = endpoints.MapGroup("/api/v{version:apiVersion}/workflows/tareas")
            .WithApiVersionSet(apiVersionSet)
            .HasApiVersion(new ApiVersion(1));

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ObtenerTareaQuery(id), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(WorkflowPermissions.TareasVer)
            .Produces<WorkflowTaskResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/pendientes", async (ISender sender, CancellationToken ct, int page = 1, int pageSize = 20) =>
        {
            var result = await sender.Send(new ListarTareasPendientesQuery(page, pageSize), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(WorkflowPermissions.TareasVer)
            .Produces<PagedResult<WorkflowTaskResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{id:guid}/resolver", async (Guid id, ResolverTareaRequest body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ResolverTareaCommand(id, body.Accion, body.Comentario), ct);
            return result.IsSuccess ? Results.Ok() : result.ToProblemDetails();
        })
            .RequireAuthorization(WorkflowPermissions.TareasResolver)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{id:guid}/delegar", async (Guid id, DelegarTareaRequest body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new DelegarTareaCommand(id, body.NuevoAsignadoUserId), ct);
            return result.IsSuccess ? Results.Ok() : result.ToProblemDetails();
        })
            .RequireAuthorization(WorkflowPermissions.TareasDelegar)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);
    }
}

internal sealed record CrearWorkflowDefinitionRequest(string Codigo, string Nombre, string? Descripcion);

internal sealed record CrearWorkflowVersionEstadoRequest(
    string Codigo, string Nombre, bool EsInicial, bool EsFinal, bool RequiereTarea, string? TituloTarea,
    Guid? AsignadoPorDefectoUserId, int? SlaMinutos, Guid? EscalarAUserId);

internal sealed record CrearWorkflowVersionTransicionRequest(
    string DesdeCodigo, string HaciaCodigo, string Accion, string? ReglaExpresion, int Orden);

internal sealed record CrearWorkflowVersionRequest(
    IReadOnlyList<CrearWorkflowVersionEstadoRequest> Estados, IReadOnlyList<CrearWorkflowVersionTransicionRequest> Transiciones);

internal sealed record IniciarInstanciaRequest(Guid WorkflowDefinitionId, Dictionary<string, string> Variables);

internal sealed record ResolverTareaRequest(string Accion, string? Comentario);

internal sealed record DelegarTareaRequest(Guid NuevoAsignadoUserId);
