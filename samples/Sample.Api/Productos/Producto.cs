using BitCode.Framework.Shared.Kernel;

namespace Sample.Api.Productos;

public class Producto : Entity<Guid>, IAuditedEntity, ISoftDelete
{
    public string Nombre { get; set; } = string.Empty;

    public decimal Precio { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    public string? DeletedBy { get; set; }

    public Producto(Guid id, string nombre, decimal precio) : base(id)
    {
        Nombre = nombre;
        Precio = precio;
    }

    private Producto()
    {
    }
}
