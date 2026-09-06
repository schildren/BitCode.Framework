using BitCode.Framework.Shared.Domain.MultiTenancy;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;

/// <summary>
/// Implementación por defecto de <see cref="ITenantContext"/> (F1-15): envuelve el
/// <see cref="ITenantProvider"/> registrado (cualquiera sea su implementación — productiva vía
/// claim JWT, nula, o de prueba) y memoiza el resultado la primera vez que se accede a
/// <see cref="TenantId"/> dentro del scope de DI actual. Registrada como <c>Scoped</c>
/// (<see cref="PersistenceServiceCollectionExtensions.AddSharedPersistence{TContext}(Microsoft.Extensions.DependencyInjection.IServiceCollection, string)"/>),
/// vive exactamente el mismo ciclo de vida que un request HTTP (o un scope manual en un job).
/// </summary>
/// <remarks>
/// La memoización es lo que garantiza la inmutabilidad "efectiva" del valor durante el scope: aunque
/// el <see cref="ITenantProvider"/> subyacente pudiera (en teoría) devolver un valor distinto en una
/// segunda llamada, <see cref="TenantContext"/> ya fijó el primero y nunca vuelve a consultarlo. No
/// existe ningún setter público — ni aquí ni en la interfaz — así que ningún handler, middleware o
/// código de aplicación puede sobrescribir el <c>TenantId</c> una vez resuelto.
/// </remarks>
public sealed class TenantContext(ITenantProvider tenantProvider) : ITenantContext
{
    private bool _resolved;
    private Guid? _tenantId;

    public bool IsMultiTenancyEnabled => tenantProvider.IsMultiTenancyEnabled;

    public Guid? TenantId
    {
        get
        {
            if (!_resolved)
            {
                _tenantId = tenantProvider.TenantId;
                _resolved = true;
            }

            return _tenantId;
        }
    }
}
