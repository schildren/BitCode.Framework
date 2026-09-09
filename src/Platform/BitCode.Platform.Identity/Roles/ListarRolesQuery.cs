using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using BitCode.Framework.Shared.Kernel;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Platform.Identity.Roles;

internal sealed record ListarRolesQuery : IQuery<IReadOnlyList<RolResponse>>;

internal sealed class ListarRolesQueryHandler(RoleManager<ApplicationRole> roleManager)
    : IRequestHandler<ListarRolesQuery, Result<IReadOnlyList<RolResponse>>>
{
    public async Task<Result<IReadOnlyList<RolResponse>>> Handle(
        ListarRolesQuery request, CancellationToken cancellationToken)
    {
        // Catálogo de roles administrables -- volumen estructuralmente acotado (regla dura 16,
        // docs/convenciones.md: ListAsync sin paginar solo es aceptable para un conjunto pequeño y
        // fijo), a diferencia del listado de usuarios (ListarUsuariosQuery), que sí pagina.
        var roles = await roleManager.Roles.OrderBy(r => r.Name).ToListAsync(cancellationToken);

        var items = new List<RolResponse>(roles.Count);
        foreach (var role in roles)
        {
            var claims = await roleManager.GetClaimsAsync(role);
            var permisos = claims
                .Where(c => c.Type == PermissionClaimTypes.Permission)
                .Select(c => c.Value)
                .ToArray();
            items.Add(new RolResponse(role.Id, role.Name ?? string.Empty, permisos));
        }

        return Result.Success<IReadOnlyList<RolResponse>>(items);
    }
}
