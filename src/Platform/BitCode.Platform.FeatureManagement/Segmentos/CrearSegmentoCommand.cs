using BitCode.Framework.Platform.FeatureManagement.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.FeatureManagement.Segmentos;

/// <summary>
/// Alta de un segmento. Un único comando cubre los dos tipos soportados (<see cref="SegmentoTipo"/>)
/// distinguidos por <see cref="Tipo"/> -- <see cref="TenantIdCriterio"/> es obligatorio solo para
/// <see cref="SegmentoTipo.PorTenant"/>, <see cref="Porcentaje"/> solo para
/// <see cref="SegmentoTipo.PorPorcentaje"/> (validado condicionalmente). Implementa
/// <see cref="IIdempotentCommand"/> (F1-22).
/// </summary>
internal sealed record CrearSegmentoCommand(string Nombre, SegmentoTipo Tipo, Guid? TenantIdCriterio, int? Porcentaje)
    : ICommand<Guid>, IIdempotentCommand;

internal sealed class CrearSegmentoCommandValidator : AbstractValidator<CrearSegmentoCommand>
{
    public CrearSegmentoCommandValidator()
    {
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(128);
        RuleFor(c => c.Tipo).IsInEnum();

        RuleFor(c => c.TenantIdCriterio)
            .NotEmpty()
            .When(c => c.Tipo == SegmentoTipo.PorTenant)
            .WithMessage("TenantIdCriterio es obligatorio para segmentos de tipo PorTenant.");

        RuleFor(c => c.Porcentaje)
            .NotNull().InclusiveBetween(0, 100)
            .When(c => c.Tipo == SegmentoTipo.PorPorcentaje)
            .WithMessage("Porcentaje (0-100) es obligatorio para segmentos de tipo PorPorcentaje.");
    }
}

internal sealed class CrearSegmentoCommandHandler(
    IRepository<Segmento, Guid> repository,
    IAuditWriter auditWriter,
    IFeatureManagementActorContext actorContext)
    : IRequestHandler<CrearSegmentoCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CrearSegmentoCommand request, CancellationToken cancellationToken)
    {
        var segmentoResult = request.Tipo switch
        {
            SegmentoTipo.PorTenant => Segmento.CrearPorTenant(Guid.NewGuid(), request.Nombre, request.TenantIdCriterio!.Value),
            SegmentoTipo.PorPorcentaje => Segmento.CrearPorPorcentaje(Guid.NewGuid(), request.Nombre, request.Porcentaje!.Value),
            _ => Result.Failure<Segmento>(Error.Validation(
                "FeatureManagement.Segmentos.TipoNoSoportado", $"Tipo de segmento no soportado: {request.Tipo}.")),
        };

        if (segmentoResult.IsFailure)
        {
            return Result.Failure<Guid>(segmentoResult.Error);
        }

        var segmento = segmentoResult.Value;
        await repository.AddAsync(segmento, cancellationToken);

        var auditRequest = new AuditEntryRequest(
            actor: actorContext.GetCurrentActor(),
            tenantId: null,
            action: "featuremanagement.segmentos.crear",
            resource: new AuditResource("featuremanagement.segmentos", segmento.Id.ToString()),
            outcome: AuditOutcome.Success,
            reason: null,
            metadata: new Dictionary<string, string?> { ["nombre"] = segmento.Nombre, ["tipo"] = segmento.Tipo.ToString() });
        _ = await auditWriter.WriteAsync(auditRequest, cancellationToken);

        return segmento.Id;
    }
}
