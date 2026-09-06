namespace BitCode.Framework.Shared.Infrastructure.Security.Audit;

/// <summary>
/// El actor que ejecutó (o intentó ejecutar) la operación auditada (F2-15, Épica F2-D). Deliberadamente
/// un tipo propio del módulo de auditoría, no una reutilización directa de <c>AbacSubject</c> (F2-08):
/// <c>AbacSubject</c> envuelve un <see cref="System.Security.Claims.ClaimsPrincipal"/> completo más los
/// permisos efectivos calculados, pensado para evaluar una decisión de autorización en el momento; un
/// <see cref="AuditEntry"/> persiste indefinidamente y solo necesita el identificador y el tipo del actor
/// -- guardar el <c>ClaimsPrincipal</c> completo (o referenciarlo) sería tanto una fuga de información
/// innecesaria (claims que no hacen falta para auditoría) como un acoplamiento a un tipo mutable ajeno al
/// propio registro inmutable. El llamador (handler de aplicación) arma este valor explícitamente a partir
/// del <c>ClaimsPrincipal</c>/identidad de servicio ya resuelta, igual que <c>AbacResource</c> (F2-08) se
/// arma explícitamente a partir de la entidad ya leída -- no hay resolución automática desde
/// <c>HttpContext</c> en este módulo.
/// </summary>
public sealed class AuditActor
{
    public AuditActor(string id, AuditActorType type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        Id = id;
        Type = type;
    }

    /// <summary>
    /// Identificador del actor -- el <c>sub</c>/<c>UserId</c> para <see cref="AuditActorType.User"/>, el
    /// <c>client_id</c> para <see cref="AuditActorType.Service"/> (F2-04), o un nombre lógico fijo para
    /// <see cref="AuditActorType.System"/> (por ejemplo, el nombre del job).
    /// </summary>
    public string Id { get; }

    public AuditActorType Type { get; }
}
