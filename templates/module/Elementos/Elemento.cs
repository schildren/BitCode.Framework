using BitCode.Framework.Shared.Kernel;

namespace MyApp.Modules.Elementos;

// Agregado de ejemplo generado por "dotnet new bitcode-module" -- reemplazalo por el/los agregado(s)
// reales de tu bounded context (podés agregar más con "dotnet new bitcode-entity"/"dotnet new
// bitcode-feature" dentro de este mismo proyecto de módulo, ver README.md). A diferencia del Elemento de
// ejemplo de "dotnet new bitcode-app" (single-tenant), este SÍ implementa ITenantEntity: los módulos de
// negocio reales (ver src/Platform/BitCode.Platform.Dashboard/Widgets/DashboardWidget.cs) son
// multi-tenant por defecto.
public class Elemento : Entity<Guid>, ITenantEntity, IAuditedEntity, ISoftDelete
{
    public string Nombre { get; set; } = string.Empty;

    public Guid TenantId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    public string? DeletedBy { get; set; }

    public Elemento(Guid id, string nombre) : base(id)
    {
        Nombre = nombre;
    }

    private Elemento()
    {
    }
}
