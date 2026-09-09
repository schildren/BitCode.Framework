using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using BitCode.Framework.Shared.Kernel;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace BitCode.Framework.Platform.Identity.Sessions;

internal sealed record ListarSesionesQuery(Guid UserId) : IQuery<IReadOnlyList<SesionResponse>>;

internal sealed class ListarSesionesQueryHandler(
    UserManager<ApplicationUser> userManager,
    IUserSessionStore sessionStore)
    : IRequestHandler<ListarSesionesQuery, Result<IReadOnlyList<SesionResponse>>>
{
    public async Task<Result<IReadOnlyList<SesionResponse>>> Handle(
        ListarSesionesQuery request, CancellationToken cancellationToken)
    {
        // Resuelve el usuario primero vía UserManager (tenant-filtrado, F1-12) antes de tocar
        // RefreshTokens -- ver el pendiente explícito documentado en IUserSessionStore sobre por qué
        // esto es necesario (RefreshToken no implementa ITenantEntity).
        var user = await userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null)
        {
            return Result.Failure<IReadOnlyList<SesionResponse>>(new Error(
                "Identidad.Usuarios.NoEncontrado", "El usuario indicado no existe.", ErrorType.NotFound));
        }

        var sessions = await sessionStore.ListActiveSessionsAsync(user.Id, cancellationToken);
        var items = sessions.Select(s => new SesionResponse(s.Id, s.CreatedAtUtc, s.ExpiresAtUtc)).ToArray();

        return Result.Success<IReadOnlyList<SesionResponse>>(items);
    }
}
