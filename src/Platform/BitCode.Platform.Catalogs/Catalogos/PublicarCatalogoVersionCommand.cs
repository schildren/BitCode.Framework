using BitCode.Framework.Platform.Catalogs.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.Catalogs.Catalogos;

/// <summary>
/// Publica una versión en borrador -- la operación sensible de referencia del módulo (checklist Fase 6,
/// requisito común 7 "RBAC y ABAC"): el endpoint exige el permiso RBAC
/// <see cref="CatalogsPermissions.VersionesPublicar"/> (distinto y más restrictivo que
/// <see cref="CatalogsPermissions.VersionesCrear"/>) vía <c>RequireAuthorization</c>, y el handler además
/// evalúa explícitamente <see cref="IAuthorizationPolicyEvaluator"/> (F2-08) con la regla ABAC
/// incorporada del framework (<c>AttributeScopeAbacRule</c>) sobre el atributo <c>catalogoId</c> -- un
/// consumidor real restringe qué catálogos puede publicar un actor cuyo rol solo administra un
/// subconjunto de catálogos, configurando <c>AbacOptions.ScopeRules</c> con
/// <c>ResourceType = "catalogos.versiones"</c>, <c>ResourceAttributeKey = "catalogoId"</c> y el
/// <c>ClaimType</c> que transporte el alcance del actor -- ver <c>docs/guia-catalogs.md</c>, sección
/// "RBAC + ABAC en la publicación de versiones". Requiere que el proyecto consumidor haya registrado
/// <c>AddSharedAbacAuthorization</c> (F2-08). Al publicar, cierra automáticamente la vigencia de la
/// versión previamente vigente del mismo catálogo (si existe) para que nunca convivan dos versiones
/// vigentes al mismo tiempo.
/// </summary>
internal sealed record PublicarCatalogoVersionCommand(Guid CatalogoVersionId, DateTime VigenteDesde, DateTime? VigenteHasta)
    : ICommand, IIdempotentCommand;

internal sealed class PublicarCatalogoVersionCommandValidator : AbstractValidator<PublicarCatalogoVersionCommand>
{
    public PublicarCatalogoVersionCommandValidator()
    {
        RuleFor(c => c.CatalogoVersionId).NotEmpty();
        RuleFor(c => c.VigenteHasta)
            .GreaterThan(c => c.VigenteDesde)
            .When(c => c.VigenteHasta is not null);
    }
}

internal sealed class PublicarCatalogoVersionCommandHandler(
    IRepository<CatalogoVersion, Guid> versionRepository,
    IAuthorizationPolicyEvaluator authorizationPolicyEvaluator,
    IAuditWriter auditWriter,
    ICatalogsActorContext actorContext)
    : IRequestHandler<PublicarCatalogoVersionCommand, Result>
{
    public const string ResourceType = "catalogos.versiones";
    public const string Action = "publicar";
    public const string CatalogoIdAttribute = "catalogoId";

    public async Task<Result> Handle(PublicarCatalogoVersionCommand request, CancellationToken cancellationToken)
    {
        var version = await versionRepository.GetByIdAsync(request.CatalogoVersionId, cancellationToken);
        if (version is null)
        {
            return Result.Failure(Error.NotFound(
                "Catalogos.Versiones.NoEncontrada", $"No existe la versión {request.CatalogoVersionId}."));
        }

        var principal = actorContext.GetCurrentPrincipal();
        if (principal is null)
        {
            // Fail-closed (mismo criterio que DesactivarEmpresaCommandHandler, Organization): sin un
            // ClaimsPrincipal resuelto no hay forma de evaluar la regla ABAC de alcance, así que la
            // operación se deniega en vez de omitir el control.
            return Result.Failure(new Error(
                "Catalogos.Versiones.ActorNoResuelto",
                "No se pudo resolver el actor autenticado para evaluar la autorización.",
                ErrorType.Forbidden));
        }

        var resource = new AbacResource(
            ResourceType,
            new Dictionary<string, object?> { [CatalogoIdAttribute] = version.CatalogoId.ToString() });

        var decision = await authorizationPolicyEvaluator.EvaluateAsync(
            principal, resource, Action, cancellationToken: cancellationToken);

        if (!decision.Allowed)
        {
            await WriteAuditEntryAsync(version, AuditOutcome.Denied, decision.Reason, cancellationToken);
            return Result.Failure(new Error(
                "Catalogos.Versiones.NoAutorizado", decision.Reason, ErrorType.Forbidden));
        }

        var vigenteAnterior = (await versionRepository.ListAsync(
            new CatalogoVersionVigenteAbiertaSpecification(version.CatalogoId), cancellationToken)).FirstOrDefault();

        // Validación de negocio esperada (regla dura 6, docs/convenciones.md): Result.Failure, nunca una
        // excepción. No se audita como "Denied" (reservado a decisiones de autorización RBAC/ABAC, ver
        // AuditOutcome) ni como "Error" (reservado a fallos técnicos inesperados) -- mismo criterio que
        // DesactivarEmpresaCommandHandler (Organization), que tampoco audita el camino NotFound.
        var publicarResult = version.Publicar(request.VigenteDesde, request.VigenteHasta);
        if (publicarResult.IsFailure)
        {
            return publicarResult;
        }

        if (vigenteAnterior is not null && vigenteAnterior.Id != version.Id)
        {
            vigenteAnterior.CerrarVigencia(request.VigenteDesde);
            versionRepository.Update(vigenteAnterior);
        }

        versionRepository.Update(version);

        await WriteAuditEntryAsync(version, AuditOutcome.Success, null, cancellationToken);

        return Result.Success();
    }

    private async Task WriteAuditEntryAsync(
        CatalogoVersion version, AuditOutcome outcome, string? reason, CancellationToken cancellationToken)
    {
        var auditRequest = new AuditEntryRequest(
            actor: actorContext.GetCurrentActor(),
            tenantId: version.TenantId == Guid.Empty ? null : version.TenantId,
            action: "catalogos.versiones.publicar",
            resource: new AuditResource(ResourceType, version.Id.ToString()),
            outcome: outcome,
            reason: reason,
            metadata: new Dictionary<string, string?>
            {
                ["catalogoId"] = version.CatalogoId.ToString(),
                ["numero"] = version.Numero.ToString(),
            });
        _ = await auditWriter.WriteAsync(auditRequest, cancellationToken);
    }
}
