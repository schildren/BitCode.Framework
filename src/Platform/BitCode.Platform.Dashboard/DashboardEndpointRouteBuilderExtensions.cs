using Asp.Versioning;
using BitCode.Framework.Platform.Dashboard.Metricas;
using BitCode.Framework.Platform.Dashboard.Widgets;
using BitCode.Framework.Shared.Infrastructure.Web.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BitCode.Framework.Platform.Dashboard;

/// <summary>
/// Mapea los endpoints HTTP del módulo Dashboard bajo <c>/api/v1/dashboard/widgets/...</c>. Todos los
/// endpoints operan exclusivamente sobre el PROPIO dashboard del actor autenticado (ver
/// <c>docs/guia-dashboard.md</c>, sección "RBAC y ownership") -- ninguno recibe ni acepta un identificador
/// de usuario ajeno.
/// </summary>
public static class DashboardEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var apiVersionSet = endpoints.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1))
            .ReportApiVersions()
            .Build();

        var group = endpoints.MapGroup("/api/v{version:apiVersion}/dashboard/widgets")
            .WithApiVersionSet(apiVersionSet)
            .HasApiVersion(new ApiVersion(1));

        group.MapGet("/", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListarWidgetsQuery(), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(DashboardPermissions.WidgetsVer)
            .Produces<IReadOnlyList<DashboardWidgetResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ObtenerWidgetQuery(id), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(DashboardPermissions.WidgetsVer)
            .Produces<DashboardWidgetResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}/metrica", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ObtenerMetricaWidgetQuery(id), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(DashboardPermissions.WidgetsVer)
            .Produces<DashboardWidgetMetricaResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/", async (AgregarWidgetRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new AgregarWidgetCommand(request.Tipo, request.Titulo, request.WorkflowDefinitionId), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/dashboard/widgets/{result.Value}", result.Value)
                : result.ToProblemDetails();
        })
            .RequireAuthorization(DashboardPermissions.WidgetsAdministrar)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new QuitarWidgetCommand(id), ct);
            return result.IsSuccess ? Results.Ok() : result.ToProblemDetails();
        })
            .RequireAuthorization(DashboardPermissions.WidgetsAdministrar)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/orden", async (ReordenarWidgetsRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ReordenarWidgetsCommand(request.WidgetIdsEnOrden), ct);
            return result.IsSuccess ? Results.Ok() : result.ToProblemDetails();
        })
            .RequireAuthorization(DashboardPermissions.WidgetsAdministrar)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        return endpoints;
    }

    private sealed record AgregarWidgetRequest(TipoWidgetDashboard Tipo, string Titulo, Guid? WorkflowDefinitionId);

    private sealed record ReordenarWidgetsRequest(IReadOnlyList<Guid> WidgetIdsEnOrden);
}
