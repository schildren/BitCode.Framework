namespace BitCode.Framework.Shared.Kernel;

/// <summary>
/// Marca una entidad que necesita control de concurrencia optimista. El framework detecta esta
/// interfaz por reflexión (mismo patrón que <see cref="IAuditedEntity"/>/<see cref="ISoftDelete"/>/
/// <see cref="ITenantEntity"/>) y configura <c>RowVersion</c> como token de concurrencia de EF Core
/// (<c>IsRowVersion()</c>) sin requerir configuración adicional en <c>OnModelCreating</c> — ver
/// <c>ConcurrencyModelConfigurator</c> en <c>Shared.Infrastructure.Persistence</c>. SQL Server
/// actualiza este valor automáticamente en cada <c>UPDATE</c>; un <c>SaveChangesAsync</c> que intente
/// escribir sobre una fila cuyo <c>RowVersion</c> ya cambió falla con un conflicto de concurrencia
/// (F1-08) que el framework traduce de forma uniforme a un <c>Result.Failure</c> con
/// <see cref="ErrorType.Conflict"/> (HTTP 409), nunca a una excepción sin controlar.
/// </summary>
public interface IHasConcurrencyToken
{
    byte[] RowVersion { get; set; }
}
