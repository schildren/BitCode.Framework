using Asp.Versioning;
using BitCode.Framework.Platform.Notifications.Envio;
using BitCode.Framework.Platform.Notifications.Plantillas;
using BitCode.Framework.Platform.Notifications.Preferencias;
using BitCode.Framework.Shared.Infrastructure.Web.Results;
using BitCode.Framework.Shared.Kernel;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BitCode.Framework.Platform.Notifications;

/// <summary>Mapea los endpoints HTTP del módulo Notifications bajo <c>/api/v1/notifications/...</c>.</summary>
public static class NotificationsEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapNotificationsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var apiVersionSet = endpoints.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1))
            .ReportApiVersions()
            .Build();

        var group = endpoints.MapGroup("/api/v{version:apiVersion}/notifications")
            .WithApiVersionSet(apiVersionSet)
            .HasApiVersion(new ApiVersion(1));

        MapPlantillas(group);
        MapNotificaciones(group);
        MapPreferencias(group);

        return endpoints;
    }

    private static void MapPlantillas(RouteGroupBuilder group)
    {
        group.MapPost("/plantillas", async (
            CrearNotificationTemplateRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CrearNotificationTemplateCommand(request.Codigo, request.Canal, request.Locale, request.Asunto, request.Cuerpo), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/notifications/plantillas/{result.Value}", result.Value)
                : result.ToProblemDetails();
        })
            .RequireAuthorization(NotificationsPermissions.PlantillasAdministrar)
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);
    }

    private static void MapNotificaciones(RouteGroupBuilder group)
    {
        var notificaciones = group.MapGroup("/notificaciones");

        notificaciones.MapPost("/enviar", async (
            EnviarNotificacionRequestDto request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new EnviarNotificacionCommand(
                    request.DestinatarioUserId, request.DestinatarioContacto, request.CodigoPlantilla,
                    request.Canal, request.Locale, request.Datos),
                ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(NotificationsPermissions.NotificacionesEnviar)
            .Produces<Guid>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        notificaciones.MapGet("/", async (
            ISender sender, CancellationToken ct, NotificationEstado? estado = null, int page = 1, int pageSize = 20) =>
        {
            var result = await sender.Send(new ListarMisNotificacionesQuery(estado, page, pageSize), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(NotificationsPermissions.NotificacionesVer)
            .Produces<PagedResult<NotificationResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        notificaciones.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ObtenerNotificacionQuery(id), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(NotificationsPermissions.NotificacionesVer)
            .Produces<NotificationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        notificaciones.MapPost("/{id:guid}/marcar-leida", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new MarcarNotificacionComoLeidaCommand(id), ct);
            return result.IsSuccess ? Results.Ok() : result.ToProblemDetails();
        })
            .RequireAuthorization(NotificationsPermissions.NotificacionesMarcarLeida)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);
    }

    private static void MapPreferencias(RouteGroupBuilder group)
    {
        var preferencias = group.MapGroup("/preferencias");

        preferencias.MapGet("/", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListarMisPreferenciasQuery(), ct);
            return result.ToOkOrProblem();
        })
            .RequireAuthorization(NotificationsPermissions.PreferenciasAdministrar)
            .Produces<IReadOnlyList<UserNotificationPreferenceResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        preferencias.MapPost("/opt-out", async (
            OptarPreferenciaRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new OptarPorNoRecibirCommand(request.CodigoPlantilla, request.Canal), ct);
            return result.IsSuccess ? Results.Ok() : result.ToProblemDetails();
        })
            .RequireAuthorization(NotificationsPermissions.PreferenciasAdministrar)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        preferencias.MapPost("/opt-in", async (
            OptarPreferenciaRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new OptarPorRecibirCommand(request.CodigoPlantilla, request.Canal), ct);
            return result.IsSuccess ? Results.Ok() : result.ToProblemDetails();
        })
            .RequireAuthorization(NotificationsPermissions.PreferenciasAdministrar)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);
    }

    private sealed record CrearNotificationTemplateRequest(
        string Codigo, NotificationChannel Canal, string Locale, string? Asunto, string Cuerpo);

    private sealed record EnviarNotificacionRequestDto(
        Guid DestinatarioUserId, string? DestinatarioContacto, string CodigoPlantilla,
        NotificationChannel Canal, string Locale, IReadOnlyDictionary<string, string> Datos);

    private sealed record OptarPreferenciaRequest(string CodigoPlantilla, NotificationChannel Canal);
}
