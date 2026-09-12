using BitCode.Framework.Platform.Identity.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace BitCode.Framework.Platform.Identity.Users;

/// <summary>
/// Alta de usuario administrable (Fase 6, módulo Identity Administration). Implementa
/// <see cref="IIdempotentCommand"/> (F1-22) -- mismo criterio que <c>CrearProductoCommand</c> del
/// piloto: un POST repetido con la misma Idempotency-Key y el mismo cuerpo no crea un segundo usuario.
/// </summary>
internal sealed record CrearUsuarioCommand(string UserName, string Email, string Password)
    : ICommand<Guid>, IIdempotentCommand;

internal sealed class CrearUsuarioCommandValidator : AbstractValidator<CrearUsuarioCommand>
{
    public CrearUsuarioCommandValidator()
    {
        RuleFor(c => c.UserName).NotEmpty();
        RuleFor(c => c.Email).NotEmpty().EmailAddress();
        RuleFor(c => c.Password).NotEmpty().MinimumLength(8);
    }
}

/// <summary>
/// No llama <c>IUnitOfWork.SaveChangesAsync</c> explícitamente por el mismo motivo de convención que
/// el resto del framework (regla dura 1, <c>docs/convenciones.md</c>): <c>UserManager.CreateAsync</c>
/// ya persiste el alta por su cuenta (Identity administra su propio <c>SaveChangesAsync</c>, fuera del
/// alcance de <c>TransactionBehavior</c>) -- el <c>SaveChangesAsync</c> adicional que
/// <c>TransactionBehavior</c> ejecuta al final del pipeline es un flush sin cambios pendientes,
/// inofensivo, no una segunda escritura del alta.
/// </summary>
internal sealed class CrearUsuarioCommandHandler(
    UserManager<ApplicationUser> userManager,
    IAuditWriter auditWriter,
    IIdentityAdministrationActorContext actorContext)
    : IRequestHandler<CrearUsuarioCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CrearUsuarioCommand request, CancellationToken cancellationToken)
    {
        var user = new ApplicationUser { UserName = request.UserName, Email = request.Email };
        var identityResult = await userManager.CreateAsync(user, request.Password);

        await WriteAuditEntryAsync(user, identityResult, cancellationToken);

        return identityResult.Succeeded
            ? user.Id
            : Result.Failure<Guid>(new Error(
                "Identidad.Usuarios.NoSePudoCrear",
                string.Join("; ", identityResult.Errors.Select(e => e.Description)),
                ErrorType.Validation));
    }

    private async Task WriteAuditEntryAsync(
        ApplicationUser user, IdentityResult identityResult, CancellationToken cancellationToken)
    {
        var request = new AuditEntryRequest(
            actor: actorContext.GetCurrentActor(),
            tenantId: user.TenantId == Guid.Empty ? null : user.TenantId,
            action: "identidad.usuarios.crear",
            resource: new AuditResource("identidad.usuarios", user.Id.ToString()),
            outcome: identityResult.Succeeded ? AuditOutcome.Success : AuditOutcome.Error,
            reason: identityResult.Succeeded
                ? null
                : string.Join("; ", identityResult.Errors.Select(e => e.Description)),
            metadata: new Dictionary<string, string?> { ["userName"] = user.UserName });

        // Fallo de auditoría descartado a propósito (mismo criterio que RoleManagerPermissionExtensions):
        // no bloquea el alta ya resuelta por un problema transitorio del almacenamiento de auditoría.
        _ = await auditWriter.WriteAsync(request, cancellationToken);
    }
}
