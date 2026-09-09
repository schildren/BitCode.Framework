using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Organization.Cargos;

/// <summary>
/// Cuarto y último nivel de la jerarquía organizacional (Empresa -&gt; Sucursal -&gt; Área -&gt;
/// <see cref="Cargo"/>). Mismo alcance simplificado que <c>Area</c> (ver
/// <c>docs/guia-organization.md</c>): CRUD básico (crear/listar), sin jerarquía propia adicional -- un
/// cargo no tiene "sub-cargos", es la hoja de la jerarquía.
/// </summary>
public sealed class Cargo : Entity<Guid>, ITenantEntity, IAuditedEntity, ISoftDelete
{
    public Guid AreaId { get; private set; }

    public string Nombre { get; private set; } = string.Empty;

    public Guid TenantId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public string? DeletedBy { get; set; }

    public Cargo(Guid id, Guid areaId, string nombre) : base(id)
    {
        AreaId = areaId;
        Nombre = nombre;
    }

    private Cargo()
    {
    }
}
