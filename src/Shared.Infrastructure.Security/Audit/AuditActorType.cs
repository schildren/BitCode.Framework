namespace BitCode.Framework.Shared.Infrastructure.Security.Audit;

/// <summary>
/// Tipo de actor que originó una operación auditada (F2-15, Épica F2-D). Distingue un usuario humano
/// autenticado de una identidad de servicio (F2-04, OAuth2 Client Credentials) o de un proceso interno
/// del propio sistema (job, migración, seeder) que actúa sin un usuario ni un servicio detrás — necesario
/// para que un registro de auditoría no fuerce a interpretar todo <see cref="AuditActor.Id"/> como un
/// identificador de usuario.
/// </summary>
public enum AuditActorType
{
    User,
    Service,
    System,
}
