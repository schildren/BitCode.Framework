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

internal sealed record QuitarPermisoDeRolCommand(string NombreRol, string Permiso) : ICommand, IIdempotentCommand;

internal sealed class QuitarPermisoDeRolCommandValidator : AbstractValidator<QuitarPermisoDeRolCommand>
{
    public QuitarPermisoDeRolCommandValidator()
    {
        RuleFor(c => c.NombreRol).NotEmpty();
        RuleFor(c => c.Permiso).NotEmpty();
    }
}

internal sealed class QuitarPermisoDeRolCommandHandler(
    RoleManager<ApplicationRole> roleManager,
    IPermissionCacheInvalidator cacheInvalidator,
    IAuditWriter auditWriter,
    IIdentityAdministrationActorContext actorContext)
    : IRequestHandler<QuitarPermisoDeRolCommand, Result>
{
    public async Task<Result> Handle(QuitarPermisoDeRolCommand request, CancellationToken cancellationToken)
    {
        var role = await roleManager.FindByNameAsync(request.NombreRol);
        if (role is null)
        {
            return Result.Failure(new Error(
                "Identidad.Roles.NoEncontrado", "El rol indicado no existe.", ErrorType.NotFound));
        }

        var identityResult = await roleManager.RemovePermissionAsync(
            role, request.Permiso, cacheInvalidator, auditWriter, actorContext.GetCurrentActor(),
            tenantId: null, cancellationToken);

        return identityResult.Succeeded
            ? Result.Success()
            : Result.Failure(new Error(
                "Identidad.Roles.NoSePudoQuitarPermiso",
                string.Join("; ", identityResult.Errors.Select(e => e.Description)),
                ErrorType.Validation));
    }
}
