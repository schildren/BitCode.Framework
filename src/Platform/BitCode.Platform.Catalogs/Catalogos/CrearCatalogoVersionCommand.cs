using BitCode.Framework.Platform.Catalogs.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.Catalogs.Catalogos;

/// <summary>Un ítem a cargar en la versión en borrador que este comando crea.</summary>
internal sealed record CatalogoItemInput(string Codigo, string Etiqueta, string? Valor, int Orden);

/// <summary>
/// Crea una nueva <see cref="CatalogoVersion"/> en estado <see cref="CatalogoVersionEstado.Borrador"/>
/// (todavía sin vigencia, no visible para un consumidor que solo lee "la versión vigente") junto con sus
/// <see cref="CatalogoItem"/> -- publicarla es un paso explícito y posterior
/// (<c>PublicarCatalogoVersionCommand</c>). Implementa <see cref="IIdempotentCommand"/> (F1-22).
/// </summary>
internal sealed record CrearCatalogoVersionCommand(Guid CatalogoId, IReadOnlyList<CatalogoItemInput> Items)
    : ICommand<Guid>, IIdempotentCommand;

internal sealed class CrearCatalogoVersionCommandValidator : AbstractValidator<CrearCatalogoVersionCommand>
{
    public CrearCatalogoVersionCommandValidator()
    {
        RuleFor(c => c.CatalogoId).NotEmpty();
        RuleFor(c => c.Items).NotEmpty();
        RuleForEach(c => c.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Codigo).NotEmpty().MaximumLength(64);
            item.RuleFor(i => i.Etiqueta).NotEmpty().MaximumLength(200);
            item.RuleFor(i => i.Valor).MaximumLength(500);
        });
    }
}

internal sealed class CrearCatalogoVersionCommandHandler(
    IReadRepository<Catalogo, Guid> catalogoRepository,
    IRepository<CatalogoVersion, Guid> versionRepository,
    IRepository<CatalogoItem, Guid> itemRepository,
    IAuditWriter auditWriter,
    ICatalogsActorContext actorContext)
    : IRequestHandler<CrearCatalogoVersionCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CrearCatalogoVersionCommand request, CancellationToken cancellationToken)
    {
        var catalogo = await catalogoRepository.GetByIdAsync(request.CatalogoId, cancellationToken);
        if (catalogo is null)
        {
            return Result.Failure<Guid>(Error.NotFound(
                "Catalogos.Catalogos.NoEncontrado", $"No existe el catálogo {request.CatalogoId}."));
        }

        var numero = await versionRepository.CountAsync(
            new VersionesDeCatalogoSpecification(request.CatalogoId), cancellationToken) + 1;

        var version = new CatalogoVersion(Guid.NewGuid(), catalogo.Id, numero);
        await versionRepository.AddAsync(version, cancellationToken);

        foreach (var item in request.Items)
        {
            await itemRepository.AddAsync(
                new CatalogoItem(Guid.NewGuid(), version.Id, item.Codigo, item.Etiqueta, item.Valor, item.Orden),
                cancellationToken);
        }

        var auditRequest = new AuditEntryRequest(
            actor: actorContext.GetCurrentActor(),
            tenantId: null,
            action: "catalogos.versiones.crear",
            resource: new AuditResource("catalogos.versiones", version.Id.ToString()),
            outcome: AuditOutcome.Success,
            reason: null,
            metadata: new Dictionary<string, string?>
            {
                ["catalogoId"] = catalogo.Id.ToString(),
                ["numero"] = numero.ToString(),
                ["cantidadItems"] = request.Items.Count.ToString(),
            });
        _ = await auditWriter.WriteAsync(auditRequest, cancellationToken);

        return version.Id;
    }
}
