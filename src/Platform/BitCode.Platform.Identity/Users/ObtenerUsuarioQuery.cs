using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using BitCode.Framework.Shared.Kernel;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace BitCode.Framework.Platform.Identity.Users;

internal sealed record ObtenerUsuarioQuery(Guid UserId) : IQuery<UsuarioResponse>;

internal sealed class ObtenerUsuarioQueryHandler(UserManager<ApplicationUser> userManager)
    : IRequestHandler<ObtenerUsuarioQuery, Result<UsuarioResponse>>
{
    public async Task<Result<UsuarioResponse>> Handle(
        ObtenerUsuarioQuery request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null)
        {
            return Result.Failure<UsuarioResponse>(new Error(
                "Identidad.Usuarios.NoEncontrado", "El usuario indicado no existe.", ErrorType.NotFound));
        }

        var roles = await userManager.GetRolesAsync(user);
        var bloqueado = await userManager.IsLockedOutAsync(user);

        return new UsuarioResponse(user.Id, user.UserName, user.Email, bloqueado, roles.ToArray());
    }
}
