using BitCode.Framework.Shared.Domain.Specifications;

namespace BitCode.Framework.Platform.IntegrationHub.Solicitudes;

internal sealed class TodasLasSolicitudesSpecification : Specification<IntegrationRequest>
{
    public TodasLasSolicitudesSpecification(Guid? connectorId, IntegrationRequestEstado? estado)
    {
        ApplyCriteria(r =>
            (connectorId == null || r.ConnectorId == connectorId) && (estado == null || r.Estado == estado));
        ApplyOrderByDescending(r => r.CreatedAtUtc);
    }
}

/// <summary>Consultada por <c>Procesamiento.IntegrationOutboundProcessorJob</c> en cada disparo
/// (IgnoreQueryFilters, cross-tenant) -- mismo criterio que
/// <c>NotificationRetryJob</c>/<c>OutboxBatchProcessor</c>. No se implementa como
/// <see cref="Specification{T}"/> porque el job consulta directamente contra el <c>DbContext</c> (ver el
/// <c>remarks</c> de <c>IntegrationOutboundProcessorJob</c> para por qué), pero se documenta acá para que
/// el criterio de filtrado quede junto al resto de las specifications del módulo.</summary>
internal sealed class LogsDeSolicitudSpecification : Specification<IntegrationRequestLog>
{
    public LogsDeSolicitudSpecification(Guid integrationRequestId)
    {
        ApplyCriteria(l => l.IntegrationRequestId == integrationRequestId);
        ApplyOrderBy(l => l.IntentoNumero);
    }
}
