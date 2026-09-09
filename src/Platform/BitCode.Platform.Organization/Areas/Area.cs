using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Organization.Areas;

/// <summary>
/// Tercer nivel de la jerarquía organizacional (Empresa -&gt; Sucursal -&gt; <see cref="Area"/> -&gt;
/// Cargo). Corte simplificado a propósito (ver <c>docs/guia-organization.md</c>, sección "Alcance del
/// primer corte"): CRUD básico (crear/listar) sin desactivación ni eventos de dominio propios --
/// por eso es un <see cref="Entity{TId}"/> simple, no un <see cref="AggregateRoot{TId}"/>.
/// <see cref="ParentAreaId"/> permite modelar sub-áreas dentro de la misma sucursal (auto-referencia
/// mínima, sin consulta de árbol recursivo expuesta todavía -- pendiente explícito).
/// </summary>
public sealed class Area : Entity<Guid>, ITenantEntity, IAuditedEntity, ISoftDelete
{
    public Guid SucursalId { get; private set; }

    public string Nombre { get; private set; } = string.Empty;

    /// <summary>Área padre dentro de la MISMA sucursal, o <see langword="null"/> si es un área de
    /// primer nivel dentro de la sucursal.</summary>
    public Guid? ParentAreaId { get; private set; }

    public Guid TenantId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public string? DeletedBy { get; set; }

    public Area(Guid id, Guid sucursalId, string nombre, Guid? parentAreaId) : base(id)
    {
        SucursalId = sucursalId;
        Nombre = nombre;
        ParentAreaId = parentAreaId;
    }

    private Area()
    {
    }
}
