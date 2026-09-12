using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.IntegrationHub.Solicitudes;

/// <summary>Monitoreo (Fase 6, módulo 9: "monitoreo" del Plan Maestro): historial completo de intentos de
/// UNA solicitud -- ver <see cref="IntegrationRequestLog"/>.</summary>
internal sealed record ListarLogsDeSolicitudQuery(Guid IntegrationRequestId) : IQuery<IReadOnlyList<IntegrationRequestLogResponse>>;

internal sealed class ListarLogsDeSolicitudQueryHandler(
    IReadRepository<IntegrationRequest, Guid> requestRepository,
    IReadRepository<IntegrationRequestLog, Guid> logRepository)
    : IRequestHandler<ListarLogsDeSolicitudQuery, Result<IReadOnlyList<IntegrationRequestLogResponse>>>
{
    public async Task<Result<IReadOnlyList<IntegrationRequestLogResponse>>> Handle(
        ListarLogsDeSolicitudQuery request, CancellationToken cancellationToken)
    {
        var existe = await requestRepository.GetByIdAsync(request.IntegrationRequestId, cancellationToken);
        if (existe is null)
        {
            return Result.Failure<IReadOnlyList<IntegrationRequestLogResponse>>(Error.NotFound(
                "IntegrationHub.Solicitudes.NoEncontrada", $"No existe la solicitud {request.IntegrationRequestId}."));
        }

        var logs = await logRepository.ListAsync(
            new LogsDeSolicitudSpecification(request.IntegrationRequestId), cancellationToken);

        return logs
            .Select(l => new IntegrationRequestLogResponse(l.Id, l.IntentoNumero, l.TimestampUtc, l.Resultado, l.CodigoHttp, l.ErrorMensaje))
            .ToList();
    }
}
