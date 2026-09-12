using BitCode.Framework.Platform.Identity.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.Identity.Users;

/// <summary>
/// Desactiva un usuario (bloqueo de login vía <c>LockoutEnd</c> de Identity, F1: no elimina ni
/// anonimiza datos -- eso sería una operación de retención/GDPR distinta, fuera de alcance de este
/// primer corte). Idempotente (F1-22): repetir la desactivación sobre un usuario ya bloqueado con la
/// misma Idempotency-Key no falla ni cambia el resultado.
/// </summary>
internal sealed record DesactivarUsuarioCommand(Guid UserId) : ICommand, IIdempotentCommand;

internal sealed class DesactivarUsuarioCommandValidator : AbstractValidator<DesactivarUsuarioCommand>
{
    public DesactivarUsuarioCommandValidator() => RuleFor(c => c.UserId).NotEmpty();
}

internal sealed class DesactivarUsuarioCommandHandler(
    Microsoft.AspNetCore.Identity.UserManager<ApplicationUser> userManager,
    IAuditWriter auditWriter,
    IIdentityAdministrationActorContext actorContext)
    : IRequestHandler<DesactivarUsuarioCommand, Result>
{
    public async Task<Result> Handle(DesactivarUsuarioCommand request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null)
        {
            return Result.Failure(new Error(
                "Identidad.Usuarios.NoEncontrado", "El usuario indicado no existe.", ErrorType.NotFound));
        }

        await userManager.SetLockoutEnabledAsync(user, true);
        var identityResult = await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);

        var auditRequest = new AuditEntryRequest(
            actor: actorContext.GetCurrentActor(),
            tenantId: user.TenantId == Guid.Empty ? null : user.TenantId,
            action: "identidad.usuarios.desactivar",
            resource: new AuditResource("identidad.usuarios", user.Id.ToString()),
            outcome: identityResult.Succeeded ? AuditOutcome.Success : AuditOutcome.Error,
            reason: identityResult.Succeeded
                ? null
                : string.Join("; ", identityResult.Errors.Select(e => e.Description)));
        _ = await auditWriter.WriteAsync(auditRequest, cancellationToken);

        return identityResult.Succeeded
            ? Result.Success()
            : Result.Failure(new Error(
                "Identidad.Usuarios.NoSePudoDesactivar",
                string.Join("; ", identityResult.Errors.Select(e => e.Description)),
                ErrorType.Validation));
    }
}
