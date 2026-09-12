using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Shared.Domain.Idempotency;

/// <summary>
/// Registro persistido de una operación ya ejecutada bajo una Idempotency-Key dada (F1-22): permite
/// detectar un reintento exacto (mismo header, mismo payload) para devolver el resultado ya obtenido
/// sin volver a ejecutar el handler, y rechazar un reintento con el mismo header pero un payload
/// distinto (reuso incorrecto de la clave) en vez de tratarlo como si fuera la misma operación.
/// </summary>
/// <remarks>
/// Implementa <see cref="ITenantEntity"/> a propósito, con el mismo criterio que cualquier entidad de
/// negocio del framework (regla dura 7 de <c>docs/convenciones.md</c>): dos tenants distintos nunca
/// deben colisionar por reutilizar la misma clave literal. <c>TenantSaveChangesInterceptor</c> asigna
/// el <c>TenantId</c> del request actual al insertar, y el filtro global de
/// <c>MultiTenancyModelConfigurator</c> restringe toda lectura al tenant del scope — ambos ya
/// aplicados automáticamente porque <c>IdempotencyModelConfigurator</c> (Shared.Infrastructure.
/// Persistence) registra este tipo en el modelo de EF Core antes de que corran esos configuradores
/// reflexivos. En un proyecto sin multi-tenancy (<c>NullTenantProvider</c>), <see cref="TenantId"/>
/// queda siempre en <see cref="Guid.Empty"/>, igual que cualquier otra entidad <c>ITenantEntity</c>.
/// </remarks>
public sealed class IdempotencyKey : ITenantEntity
{
    public Guid Id { get; init; }

    /// <summary>Valor del header <c>Idempotency-Key</c> enviado por el cliente.</summary>
    public required string Key { get; init; }

    public Guid TenantId { get; set; }

    /// <summary>
    /// SHA-256 (hex, 64 caracteres) del request completo serializado a JSON (F1-22): detecta que el
    /// cliente reutilizó la misma <see cref="Key"/> con un payload distinto, lo que
    /// <c>IdempotencyBehavior</c> rechaza como error en vez de ejecutarlo como si fuera la misma
    /// operación.
    /// </summary>
    public required string RequestHash { get; init; }

    /// <summary>
    /// JSON del valor de un <c>Result&lt;TValue&gt;</c> exitoso, o <see langword="null"/> si el
    /// comando devuelve un <c>Result</c> sin valor. Solo se persiste el resultado de una ejecución
    /// EXITOSA: un <c>Result.Failure</c> no deja ningún efecto de negocio persistido (la transacción/
    /// <c>SaveChanges</c> del comando no llega a confirmar nada), así que un reintento después de un
    /// fallo debe volver a ejecutar el handler normalmente, no devolver el fallo cacheado.
    /// </summary>
    public string? ResponseValueJson { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    /// <summary>
    /// Momento a partir del cual esta entrada se trata como inexistente (F1-22, expiración):
    /// <c>IdempotencyBehavior</c> la elimina explícitamente antes de insertar una nueva entrada con la
    /// misma clave, para no violar el índice único (<c>TenantId</c>, <see cref="Key"/>) configurado en
    /// <c>IdempotencyModelConfigurator</c>.
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }
}
