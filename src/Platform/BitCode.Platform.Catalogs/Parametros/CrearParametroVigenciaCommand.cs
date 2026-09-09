using BitCode.Framework.Platform.Catalogs.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.Catalogs.Parametros;

/// <summary>
/// Da de alta una nueva vigencia de un parámetro. Rechaza el alta con <see cref="Result.Failure"/> (error
/// de negocio esperado, nunca una excepción) si se solapa en el tiempo con una vigencia ya existente del
/// MISMO parámetro (criterio de aceptación explícito del Plan Maestro para este módulo, "Concurrencia de
/// vigencias"). Implementa <see cref="IIdempotentCommand"/> (F1-22).
/// </summary>
internal sealed record CrearParametroVigenciaCommand(Guid ParametroId, string Valor, DateTime VigenteDesde, DateTime? VigenteHasta)
    : ICommand<Guid>, IIdempotentCommand;

internal sealed class CrearParametroVigenciaCommandValidator : AbstractValidator<CrearParametroVigenciaCommand>
{
    public CrearParametroVigenciaCommandValidator()
    {
        RuleFor(c => c.ParametroId).NotEmpty();
        RuleFor(c => c.Valor).NotEmpty().MaximumLength(500);
        RuleFor(c => c.VigenteHasta)
            .GreaterThan(c => c.VigenteDesde)
            .When(c => c.VigenteHasta is not null);
    }
}

internal sealed class CrearParametroVigenciaCommandHandler(
    IReadRepository<Parametro, Guid> parametroRepository,
    IRepository<ParametroVigencia, Guid> vigenciaRepository,
    IAuditWriter auditWriter,
    ICatalogsActorContext actorContext)
    : IRequestHandler<CrearParametroVigenciaCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CrearParametroVigenciaCommand request, CancellationToken cancellationToken)
    {
        var parametro = await parametroRepository.GetByIdAsync(request.ParametroId, cancellationToken);
        if (parametro is null)
        {
            return Result.Failure<Guid>(Error.NotFound(
                "Catalogos.Parametros.NoEncontrado", $"No existe el parámetro {request.ParametroId}."));
        }

        // Validación de negocio esperada (regla dura 6, docs/convenciones.md; criterio de aceptación
        // explícito del módulo, "Concurrencia de vigencias"): Result.Failure, nunca una excepción ni una
        // restricción de base de datos que dependa de una carrera. No se audita (mismo criterio que
        // DesactivarEmpresaCommandHandler/PublicarCatalogoVersionCommandHandler: AuditOutcome solo cubre
        // éxito y decisiones de autorización RBAC/ABAC denegadas, no cada rechazo de validación).
        var seSolapa = await vigenciaRepository.AnyAsync(
            new VigenciasSuperpuestasSpecification(request.ParametroId, request.VigenteDesde, request.VigenteHasta),
            cancellationToken);
        if (seSolapa)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "Catalogos.Parametros.VigenciaSuperpuesta",
                $"La vigencia propuesta se solapa con una vigencia ya existente del parámetro {request.ParametroId}."));
        }

        var vigencia = new ParametroVigencia(
            Guid.NewGuid(), request.ParametroId, request.Valor, request.VigenteDesde, request.VigenteHasta);
        await vigenciaRepository.AddAsync(vigencia, cancellationToken);

        var auditEntry = new AuditEntryRequest(
            actor: actorContext.GetCurrentActor(),
            tenantId: null,
            action: "catalogos.parametros.vigencias.crear",
            resource: new AuditResource("catalogos.parametros.vigencias", vigencia.Id.ToString()),
            outcome: AuditOutcome.Success,
            reason: null,
            metadata: new Dictionary<string, string?>
            {
                ["parametroId"] = request.ParametroId.ToString(),
                ["vigenteDesde"] = request.VigenteDesde.ToString("O"),
            });
        _ = await auditWriter.WriteAsync(auditEntry, cancellationToken);

        return vigencia.Id;
    }
}
