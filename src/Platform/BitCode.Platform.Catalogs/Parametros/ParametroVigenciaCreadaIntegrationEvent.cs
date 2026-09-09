using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Catalogs.Parametros;

/// <summary>
/// Evento de integración público del módulo Catalogs and Parameters -- el hecho de negocio "un parámetro
/// tiene una nueva vigencia dada de alta" cruza el límite de este bounded context (otro módulo puede
/// necesitar invalidar un valor cacheado del parámetro). Registrado en <c>docs/catalogo-eventos.md</c>
/// (regla dura 27). Implementa DELIBERADAMENTE tanto <see cref="DomainEvent"/> como
/// <see cref="IIntegrationEvent"/>, mismo patrón que <c>EmpresaCreadaIntegrationEvent</c> (Organization).
/// </summary>
public sealed record ParametroVigenciaCreadaIntegrationEvent(
    Guid ParametroVigenciaId, Guid ParametroId, string Valor, DateTime VigenteDesde, DateTime? VigenteHasta)
    : DomainEvent, IIntegrationEvent, IHasPartitionKey
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public string EventType => "Catalogos.ParametroVigenciaCreada";

    public int SchemaVersion => 1;

    /// <summary>Todas las vigencias de un mismo parámetro quedan en la misma partición -- un consumidor
    /// que necesite ver las vigencias de un parámetro en orden lo obtiene sin trabajo adicional.</summary>
    public string PartitionKey => ParametroId.ToString();
}
