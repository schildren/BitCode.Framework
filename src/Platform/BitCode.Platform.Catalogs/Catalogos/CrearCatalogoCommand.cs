using BitCode.Framework.Platform.Catalogs.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.Catalogs.Catalogos;

/// <summary>
/// Alta de catálogo (identidad estable, Fase 6, módulo Catalogs and Parameters). Implementa
/// <see cref="IIdempotentCommand"/> (F1-22) -- un POST repetido con la misma Idempotency-Key y el mismo
/// cuerpo no crea un segundo catálogo.
/// </summary>
internal sealed record CrearCatalogoCommand(string Codigo, string Nombre, string? Descripcion)
    : ICommand<Guid>, IIdempotentCommand;

internal sealed class CrearCatalogoCommandValidator : AbstractValidator<CrearCatalogoCommand>
{
    public CrearCatalogoCommandValidator()
    {
        RuleFor(c => c.Codigo).NotEmpty().MaximumLength(64);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Descripcion).MaximumLength(500);
    }
}

/// <summary>
/// No llama <c>IUnitOfWork.SaveChangesAsync</c> explícitamente (regla dura 1, docs/convenciones.md):
/// <c>TransactionBehavior</c> lo hace al final del pipeline.
/// </summary>
internal sealed class CrearCatalogoCommandHandler(
    IRepository<Catalogo, Guid> repository,
    IAuditWriter auditWriter,
    ICatalogsActorContext actorContext)
    : IRequestHandler<CrearCatalogoCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CrearCatalogoCommand request, CancellationToken cancellationToken)
    {
        var duplicado = await repository.AnyAsync(new CatalogoPorCodigoSpecification(request.Codigo), cancellationToken);
        if (duplicado)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "Catalogos.Catalogos.CodigoDuplicado", $"Ya existe un catálogo con código '{request.Codigo}'."));
        }

        var catalogo = new Catalogo(Guid.NewGuid(), request.Codigo, request.Nombre, request.Descripcion);
        await repository.AddAsync(catalogo, cancellationToken);

        var auditRequest = new AuditEntryRequest(
            actor: actorContext.GetCurrentActor(),
            tenantId: null,
            action: "catalogos.catalogos.crear",
            resource: new AuditResource("catalogos.catalogos", catalogo.Id.ToString()),
            outcome: AuditOutcome.Success,
            reason: null,
            metadata: new Dictionary<string, string?> { ["codigo"] = catalogo.Codigo });
        _ = await auditWriter.WriteAsync(auditRequest, cancellationToken);

        return catalogo.Id;
    }
}
