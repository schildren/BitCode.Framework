using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Organization.Empresas;

/// <summary>
/// Evento de integración público del módulo Organization -- el hecho de negocio "una empresa fue dada
/// de alta" cruza el límite de este bounded context (registrado en
/// <c>docs/catalogo-eventos.md</c> como el primer evento productivo del repositorio, regla dura 27).
/// Implementa DELIBERADAMENTE tanto <see cref="DomainEvent"/> (para que
/// <c>OutboxSaveChangesInterceptor</c> lo recolecte de <see cref="Empresa"/>) como
/// <see cref="IIntegrationEvent"/> (para que <c>OutboxBatchProcessor</c> lo publique) -- mismo patrón
/// que <c>PedidoConfirmadoIntegrationEvent</c> (Sample.Eventing, F3-13).
/// </summary>
public sealed record EmpresaCreadaIntegrationEvent(Guid EmpresaId, string RazonSocial, string Identificador)
    : DomainEvent, IIntegrationEvent, IHasPartitionKey
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public string EventType => "Organizacion.EmpresaCreada";

    public int SchemaVersion => 1;

    /// <summary>Todos los eventos de una misma empresa (alta, desactivación, futuros cambios) quedan
    /// en la misma partición -- un consumidor que necesite orden entre eventos de la misma empresa lo
    /// obtiene sin trabajo adicional.</summary>
    public string PartitionKey => EmpresaId.ToString();
}
