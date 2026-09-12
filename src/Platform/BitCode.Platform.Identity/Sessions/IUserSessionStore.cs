using BitCode.Framework.Shared.Infrastructure.Security.Jwt;

namespace BitCode.Framework.Platform.Identity.Sessions;

/// <summary>
/// Abstracción de acceso a <see cref="RefreshToken"/> (Security 2.0, sesiones = refresh tokens
/// emitidos por <c>IJwtTokenGenerator</c>) para el módulo Identity Administration -- ningún handler
/// de MediatR inyecta <see cref="IdentityAdministrationDbContext"/> directamente (regla dura 5,
/// docs/convenciones.md); esta interfaz es la única puerta de entrada, análoga a
/// <c>IRepository&lt;T,TId&gt;</c> para una entidad que no es un <c>AggregateRoot</c> propio del
/// framework (es de Security 2.0, no de este módulo). Igual que un <c>IRepository</c> real,
/// <see cref="RevokeAsync"/> solo marca el <c>ChangeTracker</c> -- NUNCA llama
/// <c>SaveChangesAsync</c> por su cuenta (regla dura 1); el flush lo hace
/// <c>TransactionBehavior</c> vía <c>IUnitOfWork</c> al final del pipeline.
/// <para>
/// Pendiente explícito: <see cref="RefreshToken"/> no implementa <c>ITenantEntity</c> (Security 2.0,
/// Fase 2) -- el filtro global de tenant NO se aplica a esta tabla. Todas las operaciones de esta
/// interfaz están acotadas a un <c>userId</c> concreto, resuelto previamente por el llamador vía
/// <c>UserManager</c> (que sí está tenant-filtrado), por lo que no hay fuga entre tenants en el uso
/// actual -- pero un futuro método que liste TODAS las sesiones sin acotar por usuario sí la tendría.
/// Ver <c>docs/guia-identity-administration.md</c>, sección "Pendientes".
/// </para>
/// </summary>
internal interface IUserSessionStore
{
    Task<IReadOnlyList<RefreshToken>> ListActiveSessionsAsync(Guid userId, CancellationToken cancellationToken);

    Task<RefreshToken?> FindByIdAsync(Guid sessionId, CancellationToken cancellationToken);

    Task RevokeAsync(RefreshToken session, CancellationToken cancellationToken);
}
