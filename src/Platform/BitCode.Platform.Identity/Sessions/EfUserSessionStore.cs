using BitCode.Framework.Shared.Infrastructure.Security.Jwt;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Platform.Identity.Sessions;

internal sealed class EfUserSessionStore(IdentityAdministrationDbContext dbContext) : IUserSessionStore
{
    public async Task<IReadOnlyList<RefreshToken>> ListActiveSessionsAsync(
        Guid userId, CancellationToken cancellationToken) =>
        await dbContext.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null && t.ExpiresAtUtc > DateTime.UtcNow)
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public Task<RefreshToken?> FindByIdAsync(Guid sessionId, CancellationToken cancellationToken) =>
        dbContext.RefreshTokens.FirstOrDefaultAsync(t => t.Id == sessionId, cancellationToken);

    public Task RevokeAsync(RefreshToken session, CancellationToken cancellationToken)
    {
        // Solo marca el ChangeTracker -- ningún SaveChangesAsync explícito acá (regla dura 1/5,
        // docs/convenciones.md): el flush lo hace TransactionBehavior vía IUnitOfWork al final del
        // pipeline, en el mismo SaveChangesAsync que persiste cualquier otro efecto del handler.
        session.RevokedAtUtc = DateTime.UtcNow;
        return Task.CompletedTask;
    }
}
