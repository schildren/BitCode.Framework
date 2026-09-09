using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Organization.Sucursales;

/// <summary>
/// Evento de integración público del módulo Organization -- "una sucursal fue dada de alta". Registrado
/// en <c>docs/catalogo-eventos.md</c> junto con los eventos de <c>Empresa</c>.
/// </summary>
public sealed record SucursalCreadaIntegrationEvent(Guid SucursalId, Guid EmpresaId, string Nombre)
    : DomainEvent, IIntegrationEvent, IHasPartitionKey
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public string EventType => "Organizacion.SucursalCreada";

    public int SchemaVersion => 1;

    public string PartitionKey => SucursalId.ToString();
}
