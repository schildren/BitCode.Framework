using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Organization.Empresas;

/// <summary>
/// Evento de integración público del módulo Organization -- el hecho de negocio "una empresa fue
/// desactivada" (operación sensible, ver <see cref="Empresa.Desactivar"/>). Registrado en
/// <c>docs/catalogo-eventos.md</c> junto con <see cref="EmpresaCreadaIntegrationEvent"/>.
/// </summary>
public sealed record EmpresaDesactivadaIntegrationEvent(Guid EmpresaId)
    : DomainEvent, IIntegrationEvent, IHasPartitionKey
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public string EventType => "Organizacion.EmpresaDesactivada";

    public int SchemaVersion => 1;

    public string PartitionKey => EmpresaId.ToString();
}
