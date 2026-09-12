using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Organization.Sucursales;

/// <summary>
/// Segundo nivel de la jerarquía organizacional (Empresa -&gt; <see cref="Sucursal"/> -&gt; Área -&gt;
/// Cargo). <see cref="EmpresaId"/> es la referencia al nivel superior -- este primer corte no navega la
/// relación vía EF Core (sin propiedad de navegación <c>Empresa</c>), solo guarda el FK, mismo criterio
/// de simplicidad que <c>docs/guia-organization.md</c> documenta como alcance explícito.
/// </summary>
public sealed class Sucursal : AggregateRoot<Guid>, ITenantEntity, IAuditedEntity, ISoftDelete
{
    public Guid EmpresaId { get; private set; }

    public string Nombre { get; private set; } = string.Empty;

    public string? Direccion { get; private set; }

    public bool Activa { get; private set; } = true;

    public Guid TenantId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public string? DeletedBy { get; set; }

    public Sucursal(Guid id, Guid empresaId, string nombre, string? direccion) : base(id)
    {
        EmpresaId = empresaId;
        Nombre = nombre;
        Direccion = direccion;

        RaiseDomainEvent(new SucursalCreadaIntegrationEvent(Id, EmpresaId, Nombre));
    }

    private Sucursal()
    {
    }

    public void Desactivar() => Activa = false;
}
