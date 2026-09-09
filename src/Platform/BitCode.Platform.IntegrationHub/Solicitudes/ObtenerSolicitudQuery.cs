using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.IntegrationHub.Solicitudes;

/// <summary>
/// Detalle de UNA solicitud de integración -- SIN verificación de ownership adicional más allá del
/// permiso RBAC genérico <see cref="IntegrationHubPermissions.SolicitudesVer"/>, a diferencia de
/// <c>ObtenerNotificacionQuery</c> (Fase 6, módulo 8) u <c>ObtenerTareaQuery</c> (Task Inbox): una
/// <see cref="IntegrationRequest"/> no es un dato PERSONAL de ningún usuario final (no tiene "dueño" en el
/// sentido de bandeja/notificación individual) -- es un dato operacional de integración entre sistemas,
/// mismo criterio de autorización que ya aplican las consolas administrativas de Workflow/Catalogs (solo
/// RBAC, sin ABAC de ownership). <see cref="IntegrationRequest.DisparadoPorUserId"/> existe para
/// trazabilidad/auditoría, no para restringir quién puede leer la fila.
/// </summary>
internal sealed record ObtenerSolicitudQuery(Guid Id) : IQuery<IntegrationRequestResponse>;

internal sealed class ObtenerSolicitudQueryHandler(IReadRepository<IntegrationRequest, Guid> repository)
    : IRequestHandler<ObtenerSolicitudQuery, Result<IntegrationRequestResponse>>
{
    public async Task<Result<IntegrationRequestResponse>> Handle(ObtenerSolicitudQuery request, CancellationToken cancellationToken)
    {
        var integrationRequest = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (integrationRequest is null)
        {
            return Result.Failure<IntegrationRequestResponse>(Error.NotFound(
                "IntegrationHub.Solicitudes.NoEncontrada", $"No existe la solicitud {request.Id}."));
        }

        return Map(integrationRequest);
    }

    internal static IntegrationRequestResponse Map(IntegrationRequest r) => new(
        r.Id, r.ConnectorId, r.DisparadoPorUserId, r.PayloadInternoJson, r.PayloadExternoJson, r.Estado,
        r.IntentosRealizados, r.UltimoErrorMensaje, r.UltimoCodigoHttp, r.EnviadaAtUtc);
}
