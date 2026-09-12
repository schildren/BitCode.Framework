using BitCode.Framework.Platform.Identity.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.Identity.Sessions;

internal sealed record RevocarSesionCommand(Guid SessionId) : ICommand, IIdempotentCommand;

internal sealed class RevocarSesionCommandValidator : AbstractValidator<RevocarSesionCommand>
{
    public RevocarSesionCommandValidator() => RuleFor(c => c.SessionId).NotEmpty();
}

internal sealed class RevocarSesionCommandHandler(
    IUserSessionStore sessionStore,
    IAuditWriter auditWriter,
    IIdentityAdministrationActorContext actorContext)
    : IRequestHandler<RevocarSesionCommand, Result>
{
    public async Task<Result> Handle(RevocarSesionCommand request, CancellationToken cancellationToken)
    {
        var session = await sessionStore.FindByIdAsync(request.SessionId, cancellationToken);
        if (session is null)
        {
            return Result.Failure(new Error(
                "Identidad.Sesiones.NoEncontrada", "La sesión indicada no existe.", ErrorType.NotFound));
        }

        if (!session.IsActive)
        {
            // Idempotente a nivel de negocio, además de la Idempotency-Key (F1-22): revocar una
            // sesión ya revocada/expirada no es un error, es un no-op exitoso.
            return Result.Success();
        }

        await sessionStore.RevokeAsync(session, cancellationToken);

        var auditRequest = new AuditEntryRequest(
            actor: actorContext.GetCurrentActor(),
            tenantId: null,
            action: "identidad.sesiones.revocar",
            resource: new AuditResource("identidad.sesiones", session.Id.ToString()),
            outcome: AuditOutcome.Success,
            metadata: new Dictionary<string, string?> { ["userId"] = session.UserId.ToString() });
        _ = await auditWriter.WriteAsync(auditRequest, cancellationToken);

        return Result.Success();
    }
}
