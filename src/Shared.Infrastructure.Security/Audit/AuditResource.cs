namespace BitCode.Framework.Shared.Infrastructure.Security.Audit;

/// <summary>
/// El recurso de negocio afectado por la operación auditada (F2-15, Épica F2-D). <see cref="Type"/> sigue
/// la misma convención de nombre que un recurso RBAC/ABAC (<c>"{entidad}"</c>, ver
/// <c>docs/convenciones.md</c> y <c>AbacResource</c>, F2-08) para que una operación auditada sea
/// correlacionable con el permiso/regla ABAC que la autorizó, sin necesitar el resto de los atributos de
/// negocio que <c>AbacResource</c> sí carga (monto, empresa, sucursal) -- esos atributos, si son
/// relevantes para la auditoría de un caso puntual, van en <see cref="AuditEntryRequest.Metadata"/>, no
/// acá.
/// </summary>
public sealed class AuditResource
{
    public AuditResource(string type, string? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        Type = type;
        Id = id;
    }

    /// <summary>Tipo del recurso (por ejemplo, <c>"pedidos"</c>).</summary>
    public string Type { get; }

    /// <summary>
    /// Identificador de la instancia concreta del recurso, si la operación auditada actúa sobre una
    /// instancia ya existente (por ejemplo, el Id de un pedido). <see langword="null"/> para una operación
    /// de creación, donde el recurso todavía no tiene identidad al momento de auditar el intento.
    /// </summary>
    public string? Id { get; }
}
