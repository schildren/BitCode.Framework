using BitCode.Framework.Platform.Organization.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.Organization.Empresas;

/// <summary>
/// Alta de empresa (Fase 6, módulo Organization). Implementa <see cref="IIdempotentCommand"/> (F1-22)
/// -- un POST repetido con la misma Idempotency-Key y el mismo cuerpo no crea una segunda empresa.
/// </summary>
internal sealed record CrearEmpresaCommand(string RazonSocial, string Identificador)
    : ICommand<Guid>, IIdempotentCommand;

internal sealed class CrearEmpresaCommandValidator : AbstractValidator<CrearEmpresaCommand>
{
    public CrearEmpresaCommandValidator()
    {
        RuleFor(c => c.RazonSocial).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Identificador).NotEmpty().MaximumLength(32);
    }
}

/// <summary>
/// No llama <c>IUnitOfWork.SaveChangesAsync</c> explícitamente (regla dura 1, docs/convenciones.md):
/// <c>TransactionBehavior</c> lo hace al final del pipeline, dentro del mismo <c>SaveChangesAsync</c>
/// que <c>OutboxSaveChangesInterceptor</c> usa para persistir <see cref="EmpresaCreadaIntegrationEvent"/>.
/// </summary>
internal sealed class CrearEmpresaCommandHandler(
    IRepository<Empresa, Guid> repository,
    IAuditWriter auditWriter,
    IOrganizationActorContext actorContext)
    : IRequestHandler<CrearEmpresaCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CrearEmpresaCommand request, CancellationToken cancellationToken)
    {
        var empresa = new Empresa(Guid.NewGuid(), request.RazonSocial, request.Identificador);
        await repository.AddAsync(empresa, cancellationToken);

        var auditRequest = new AuditEntryRequest(
            actor: actorContext.GetCurrentActor(),
            tenantId: null,
            action: "organizacion.empresas.crear",
            resource: new AuditResource("organizacion.empresas", empresa.Id.ToString()),
            outcome: AuditOutcome.Success,
            reason: null,
            metadata: new Dictionary<string, string?> { ["razonSocial"] = empresa.RazonSocial });
        _ = await auditWriter.WriteAsync(auditRequest, cancellationToken);

        return empresa.Id;
    }
}
