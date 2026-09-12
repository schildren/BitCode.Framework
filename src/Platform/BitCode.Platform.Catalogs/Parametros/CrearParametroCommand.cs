using BitCode.Framework.Platform.Catalogs.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.Catalogs.Parametros;

/// <summary>
/// Alta de parámetro (identidad estable, sin valor propio -- el valor vigente se resuelve vía
/// <see cref="ParametroVigencia"/>). Implementa <see cref="IIdempotentCommand"/> (F1-22).
/// </summary>
internal sealed record CrearParametroCommand(string Codigo, string Nombre, string? Descripcion)
    : ICommand<Guid>, IIdempotentCommand;

internal sealed class CrearParametroCommandValidator : AbstractValidator<CrearParametroCommand>
{
    public CrearParametroCommandValidator()
    {
        RuleFor(c => c.Codigo).NotEmpty().MaximumLength(64);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Descripcion).MaximumLength(500);
    }
}

internal sealed class CrearParametroCommandHandler(
    IRepository<Parametro, Guid> repository,
    IAuditWriter auditWriter,
    ICatalogsActorContext actorContext)
    : IRequestHandler<CrearParametroCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CrearParametroCommand request, CancellationToken cancellationToken)
    {
        var duplicado = await repository.AnyAsync(new ParametroPorCodigoSpecification(request.Codigo), cancellationToken);
        if (duplicado)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "Catalogos.Parametros.CodigoDuplicado", $"Ya existe un parámetro con código '{request.Codigo}'."));
        }

        var parametro = new Parametro(Guid.NewGuid(), request.Codigo, request.Nombre, request.Descripcion);
        await repository.AddAsync(parametro, cancellationToken);

        var auditRequest = new AuditEntryRequest(
            actor: actorContext.GetCurrentActor(),
            tenantId: null,
            action: "catalogos.parametros.crear",
            resource: new AuditResource("catalogos.parametros", parametro.Id.ToString()),
            outcome: AuditOutcome.Success,
            reason: null,
            metadata: new Dictionary<string, string?> { ["codigo"] = parametro.Codigo });
        _ = await auditWriter.WriteAsync(auditRequest, cancellationToken);

        return parametro.Id;
    }
}
