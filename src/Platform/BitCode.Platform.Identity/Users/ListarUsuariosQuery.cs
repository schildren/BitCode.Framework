using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using BitCode.Framework.Shared.Kernel;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Platform.Identity.Users;

/// <summary>
/// F1-21: page/pageSize crudos del cliente HTTP se validan acá (<see cref="PageRequest.Create"/>)
/// antes de tocar la base -- un pageSize fuera de rango nunca se trunca en silencio.
/// <c>UserManager&lt;ApplicationUser&gt;.Users</c> (no <c>IReadRepository&lt;,&gt;</c>: este módulo no
/// registra <c>AddSharedPersistence</c> para <see cref="IdentityAdministrationDbContext"/>, que no es
/// un <c>MultiTenantDbContext</c>) ya viene filtrado por tenant vía el mismo filtro global de EF Core
/// que el resto del contexto (F1-12) -- es la superficie de consulta que Identity expone para no
/// exponer <c>DbContext</c> directamente desde un handler (regla dura 5, docs/convenciones.md).
/// </summary>
internal sealed record ListarUsuariosQuery(int Page, int PageSize) : IQuery<PagedResult<UsuarioResponse>>;

internal sealed class ListarUsuariosQueryHandler(UserManager<ApplicationUser> userManager)
    : IRequestHandler<ListarUsuariosQuery, Result<PagedResult<UsuarioResponse>>>
{
    public async Task<Result<PagedResult<UsuarioResponse>>> Handle(
        ListarUsuariosQuery request, CancellationToken cancellationToken)
    {
        var pageRequestResult = PageRequest.Create(request.Page, request.PageSize);
        if (pageRequestResult.IsFailure)
        {
            return Result.Failure<PagedResult<UsuarioResponse>>(pageRequestResult.Error);
        }

        var pageRequest = pageRequestResult.Value;
        var query = userManager.Users.OrderBy(u => u.UserName);

        var totalCount = await query.CountAsync(cancellationToken);
        var users = await query
            .Skip((pageRequest.Page - 1) * pageRequest.PageSize)
            .Take(pageRequest.PageSize)
            .ToListAsync(cancellationToken);

        // Deuda conocida (N+1): dos roundtrips extra por fila (roles + lockout) via UserManager, hasta
        // 2*pageSize por página. Aceptado en este primer corte por simplicidad sobre UserManager;
        // ver docs/guia-identity-administration.md, "Pendientes explícitos".
        var items = new List<UsuarioResponse>(users.Count);
        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            var bloqueado = await userManager.IsLockedOutAsync(user);
            items.Add(new UsuarioResponse(user.Id, user.UserName, user.Email, bloqueado, roles.ToArray()));
        }

        return new PagedResult<UsuarioResponse>(items, pageRequest.Page, pageRequest.PageSize, totalCount);
    }
}
