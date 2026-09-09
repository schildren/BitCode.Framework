using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Catalogs.Catalogos;

/// <summary>
/// Evento de integración público del módulo Catalogs and Parameters -- el hecho de negocio "una versión
/// de catálogo quedó publicada y vigente" cruza el límite de este bounded context: otros módulos que
/// consumen este catálogo (por ejemplo, cacheando sus ítems) pueden suscribirse para invalidar su copia
/// sin tener que sondear el endpoint de lectura. Registrado en <c>docs/catalogo-eventos.md</c> (regla
/// dura 27) junto con <see cref="Parametros.ParametroVigenciaCreadaIntegrationEvent"/> como los primeros
/// eventos productivos del módulo. Implementa DELIBERADAMENTE tanto <see cref="DomainEvent"/> (para que
/// <c>OutboxSaveChangesInterceptor</c> lo recolecte de <see cref="CatalogoVersion"/>) como
/// <see cref="IIntegrationEvent"/> (para que <c>OutboxBatchProcessor</c> lo publique), mismo patrón que
/// <c>EmpresaCreadaIntegrationEvent</c> (Organization, Fase 6 módulo 2).
/// </summary>
public sealed record CatalogoVersionPublicadaIntegrationEvent(
    Guid CatalogoVersionId, Guid CatalogoId, int Numero, DateTime VigenteDesde, DateTime? VigenteHasta)
    : DomainEvent, IIntegrationEvent, IHasPartitionKey
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public string EventType => "Catalogos.CatalogoVersionPublicada";

    public int SchemaVersion => 1;

    /// <summary>Todos los eventos de un mismo catálogo (publicación de sus sucesivas versiones) quedan
    /// en la misma partición -- un consumidor que necesite ver las publicaciones de un catálogo en
    /// orden lo obtiene sin trabajo adicional.</summary>
    public string PartitionKey => CatalogoId.ToString();
}
