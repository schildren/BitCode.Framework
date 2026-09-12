namespace BitCode.Framework.Shared.Infrastructure.Security.Audit;

/// <summary>
/// Resultado de la operación auditada (F2-15, Épica F2-D). <see cref="Denied"/> es distinto de
/// <see cref="Error"/>: el primero es una decisión de autorización esperada (RBAC/ABAC, F2-07/F2-08,
/// deniega la operación) y el segundo es un fallo técnico inesperado durante la ejecución -- separarlos
/// permite que una consulta de auditoría futura (F2-20) distinga "se le negó el acceso" de "la operación
/// falló por una excepción".
/// </summary>
public enum AuditOutcome
{
    Success,
    Denied,
    Error,
}
