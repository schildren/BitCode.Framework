using BitCode.Framework.Platform.Identity.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace BitCode.Framework.Platform.Identity.Roles;

internal sealed record CrearRolCommand(string NombreRol) : ICommand<Guid>, IIdempotentCommand;

internal sealed class CrearRolCommandValidator : AbstractValidator<CrearRolCommand>
{
    public CrearRolCommandValidator() => RuleFor(c => c.NombreRol).NotEmpty();
}

internal sealed class CrearRolCommandHandler(
    RoleManager<ApplicationRole> roleManager,
    IAuditWriter auditWriter,
    IIdentityAdministrationActorContext actorContext)
    : IRequestHandler<CrearRolCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CrearRolCommand request, CancellationToken cancellationToken)
    {
        var role = new ApplicationRole(request.NombreRol);
        var identityResult = await roleManager.CreateAsync(role);

        var auditRequest = new AuditEntryRequest(
            actor: actorContext.GetCurrentActor(),
            tenantId: null,
            action: "identidad.roles.crear",
            resource: new AuditResource("identidad.roles", role.Id.ToString()),
            outcome: identityResult.Succeeded ? AuditOutcome.Success : AuditOutcome.Error,
            reason: identityResult.Succeeded
                ? null
                : string.Join("; ", identityResult.Errors.Select(e => e.Description)),
            metadata: new Dictionary<string, string?> { ["nombreRol"] = request.NombreRol });
        _ = await auditWriter.WriteAsync(auditRequest, cancellationToken);

        return identityResult.Succeeded
            ? role.Id
            : Result.Failure<Guid>(new Error(
                "Identidad.Roles.NoSePudoCrear",
                string.Join("; ", identityResult.Errors.Select(e => e.Description)),
                ErrorType.Validation));
    }
}
