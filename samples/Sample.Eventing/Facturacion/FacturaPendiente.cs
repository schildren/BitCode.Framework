using BitCode.Framework.Shared.Kernel;

namespace Sample.Eventing.Facturacion;

/// <summary>
/// F3-13 (prueba de referencia): entidad propia del Módulo B (Facturación) — el efecto de negocio
/// verificable que demuestra la consistencia eventual entre ambos módulos: cuando el Módulo A confirma
/// un <c>Pedido</c> (evento <c>PedidoConfirmadoIntegrationEvent</c>), el Módulo B crea SU PROPIA
/// entidad (no comparte tabla ni agregado con Pedidos: cada bounded context es dueño de su propio
/// modelo, comunicándose únicamente por el contrato público del evento de integración).
/// </summary>
public class FacturaPendiente : Entity<Guid>, ITenantEntity
{
    public Guid PedidoId { get; private set; }

    public string Cliente { get; private set; } = string.Empty;

    public decimal Monto { get; private set; }

    public Guid TenantId { get; set; }

    public FacturaPendiente(Guid id, Guid pedidoId, string cliente, decimal monto) : base(id)
    {
        PedidoId = pedidoId;
        Cliente = cliente;
        Monto = monto;
    }

    private FacturaPendiente()
    {
    }
}
