using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Sample.Eventing.Facturacion;
using Sample.Eventing.Pedidos;

namespace Sample.Eventing;

/// <summary>
/// F3-13 (prueba de referencia): un único <c>DbContext</c> hospeda ambos módulos de ejemplo
/// (Pedidos y Facturación) por simplicidad de este sample — en un consumidor real, cada bounded
/// context suele tener su propio <c>DbContext</c> (e incluso su propia base de datos); lo que garantiza
/// el desacople real entre ambos NO es la base de datos separada sino que Facturación nunca hace un
/// join/consulta directa contra las tablas de Pedidos: solo conoce
/// <c>PedidoConfirmadoIntegrationEvent</c> (el contrato público) y persiste su propia
/// <see cref="FacturaPendiente"/>.
/// </summary>
public class SampleEventingDbContext(DbContextOptions<SampleEventingDbContext> options, ITenantProvider tenantProvider)
    : MultiTenantDbContext(options, tenantProvider)
{
    public DbSet<Pedido> Pedidos => Set<Pedido>();

    public DbSet<FacturaPendiente> FacturasPendientes => Set<FacturaPendiente>();
}
