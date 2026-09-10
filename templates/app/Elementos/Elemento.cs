using BitCode.Framework.Shared.Kernel;

namespace AppName.Elementos;

// Feature de ejemplo generado por "dotnet new bitcode-app" -- reemplazalo por el dominio real de tu
// aplicación (o eliminalo una vez que tengas tu primer feature propio, ver
// docs/convenciones.md y "dotnet new bitcode-feature"/"dotnet new bitcode-entity" para agregar más).
public class Elemento : Entity<Guid>, IAuditedEntity, ISoftDelete
{
    public string Nombre { get; set; } = string.Empty;

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
