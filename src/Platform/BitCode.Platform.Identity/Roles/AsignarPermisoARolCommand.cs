using BitCode.Framework.Platform.Identity.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace BitCode.Framework.Platform.Identity.Roles;

/// <summary>
/// Concede un permiso a un rol reutilizando <c>RoleManagerPermissionExtensions.AddPermissionAsync</c>
/// (F2-09, Security 2.0) -- invalida el cache de permisos del rol y audita, sin reimplementar ninguna
/// de las dos cosas.
/// </summary>
internal sealed record AsignarPermisoARolCommand(string NombreRol, string Permiso) : ICommand, IIdempotentCommand;

internal sealed class AsignarPermisoARolCommandValidator : AbstractValidator<AsignarPermisoARolCommand>
{
    public AsignarPermisoARolCommandValidator()
    {
        RuleFor(c => c.NombreRol).NotEmpty();
        RuleFor(c => c.Permiso).NotEmpty();
    }
}

internal sealed class AsignarPermisoARolCommandHandler(
    RoleManager<ApplicationRole> roleManager,
    IPermissionCacheInvalidator cacheInvalidator,
    IAuditWriter auditWriter,
    IIdentityAdministrationActorContext actorContext)
    : IRequestHandler<AsignarPermisoARolCommand, Result>
{
    public async Task<Result> Handle(AsignarPermisoARolCommand request, CancellationToken cancellationToken)
    {
        var role = await roleManager.FindByNameAsync(request.NombreRol);
        if (role is null)
        {
            return Result.Failure(new Error(
                "Identidad.Roles.NoEncontrado", "El rol indicado no existe.", ErrorType.NotFound));
        }

        var identityResult = await roleManager.AddPermissionAsync(
            role, request.Permiso, cacheInvalidator, auditWriter, actorContext.GetCurrentActor(),
            tenantId: null, cancellationToken);

        return identityResult.Succeeded
            ? Result.Success()
            : Result.Failure(new Error(
                "Identidad.Roles.NoSePudoAsignarPermiso",
                string.Join("; ", identityResult.Errors.Select(e => e.Description)),
                ErrorType.Validation));
    }
}
