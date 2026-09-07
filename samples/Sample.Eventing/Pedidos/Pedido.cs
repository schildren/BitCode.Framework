using BitCode.Framework.Shared.Kernel;

namespace Sample.Eventing.Pedidos;

/// <summary>
/// F3-13 (prueba de referencia): agregado real del Módulo A (Pedidos), usado para demostrar el flujo
/// completo de eventos de integración de punta a punta (a diferencia de los eventos de test "sueltos"
/// de F3-01 a F3-11, que no levantan el evento desde ningún agregado real de negocio).
/// </summary>
public class Pedido : AggregateRoot<Guid>, ITenantEntity
{
    public string Cliente { get; private set; } = string.Empty;

    public decimal Monto { get; private set; }

    public bool Confirmado { get; private set; }

    public Guid TenantId { get; set; }

    public Pedido(Guid id, string cliente, decimal monto) : base(id)
    {
        Cliente = cliente;
        Monto = monto;
    }

    private Pedido()
    {
    }

    /// <summary>
    /// Operación de negocio real: marca el pedido como confirmado y levanta el evento de integración
    /// correspondiente (F1-23, <c>RaiseDomainEvent</c>) — es el interceptor de Outbox, no este método,
    /// quien lo escribe como fila <c>OutboxMessage</c> dentro del mismo <c>SaveChangesAsync</c> que
    /// persiste este cambio de negocio.
    /// </summary>
    public void Confirmar()
    {
        if (Confirmado)
        {
            return;
        }

        Confirmado = true;
        RaiseDomainEvent(new PedidoConfirmadoIntegrationEvent(Id, Cliente, Monto));
    }
}
