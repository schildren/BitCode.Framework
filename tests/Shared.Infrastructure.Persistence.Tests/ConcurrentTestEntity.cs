using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests;

/// <summary>
/// Entidad de prueba con el marcador de concurrencia optimista (F1-08). Deliberadamente no implementa
/// IAuditedEntity/ISoftDelete/ITenantEntity para aislar el comportamiento de IHasConcurrencyToken del
/// resto de las convenciones ya cubiertas por TestEntity.
/// </summary>
public class ConcurrentTestEntity : Entity<Guid>, IHasConcurrencyToken
{
    public string Name { get; set; } = string.Empty;

    public byte[] RowVersion { get; set; } = [];

    public ConcurrentTestEntity(Guid id, string name) : base(id)
    {
        Name = name;
    }

    private ConcurrentTestEntity()
    {
    }
}
