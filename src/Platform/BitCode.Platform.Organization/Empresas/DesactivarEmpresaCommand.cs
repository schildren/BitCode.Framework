using BitCode.Framework.Platform.Organization.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.Organization.Empresas;

/// <summary>
/// Desactiva una empresa completa -- la operación sensible de referencia del módulo (checklist Fase 6,
/// requisito común 7 "RBAC y ABAC"): el endpoint exige el permiso RBAC
/// <see cref="OrganizationPermissions.EmpresasDesactivar"/> (distinto y más restrictivo que
/// <see cref="OrganizationPermissions.SucursalesCrear"/>/<see cref="OrganizationPermissions.EmpresasCrear"/>)
/// vía <c>RequireAuthorization</c>, y el handler además evalúa explícitamente
/// <see cref="IAuthorizationPolicyEvaluator"/> (F2-08) con la regla ABAC incorporada del framework
/// (<c>AttributeScopeAbacRule</c>) sobre el atributo <c>empresaId</c> -- un consumidor real restringe
/// qué empresas puede desactivar un actor cuyo rol solo administra un subconjunto de empresas del
/// mismo tenant (grupo corporativo con varias razones sociales), configurando
/// <c>AbacOptions.ScopeRules</c> con <c>ResourceType = "organizacion.empresas"</c>,
/// <c>ResourceAttributeKey = "empresaId"</c> y el <c>ClaimType</c> que transporte el alcance del actor
/// -- ver <c>docs/guia-organization.md</c>, sección "RBAC + ABAC en la desactivación de empresas".
/// Requiere que el proyecto consumidor haya registrado <c>AddSharedAbacAuthorization</c> (F2-08).
/// </summary>
internal sealed record DesactivarEmpresaCommand(Guid Id) : ICommand, IIdempotentCommand;

internal sealed class DesactivarEmpresaCommandValidator : AbstractValidator<DesactivarEmpresaCommand>
{
    public DesactivarEmpresaCommandValidator() => RuleFor(c => c.Id).NotEmpty();
}

internal sealed class DesactivarEmpresaCommandHandler(
    IRepository<Empresa, Guid> repository,
    IAuthorizationPolicyEvaluator authorizationPolicyEvaluator,
    IAuditWriter auditWriter,
    IOrganizationActorContext actorContext)
    : IRequestHandler<DesactivarEmpresaCommand, Result>
{
    public const string ResourceType = "organizacion.empresas";
    public const string Action = "desactivar";
    public const string EmpresaIdAttribute = "empresaId";

    public async Task<Result> Handle(DesactivarEmpresaCommand request, CancellationToken cancellationToken)
    {
        var empresa = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (empresa is null)
        {
            return Result.Failure(Error.NotFound(
                "Organizacion.Empresas.NoEncontrada", $"No existe la empresa {request.Id}."));
        }

        var principal = actorContext.GetCurrentPrincipal();
        if (principal is null)
        {
            // Fail-closed (mismo criterio que SelfRoleAssignmentAbacRule/StepUpAbacRule, F2-10): sin un
            // ClaimsPrincipal resuelto no hay forma de evaluar la regla ABAC de alcance, así que la
            // operación se deniega en vez de omitir el control.
            return Result.Failure(new Error(
                "Organizacion.Empresas.ActorNoResuelto",
                "No se pudo resolver el actor autenticado para evaluar la autorización.",
                ErrorType.Forbidden));
        }

        var resource = new AbacResource(
            ResourceType,
            new Dictionary<string, object?> { [EmpresaIdAttribute] = empresa.Id.ToString() });

        var decision = await authorizationPolicyEvaluator.EvaluateAsync(
            principal, resource, Action, cancellationToken: cancellationToken);

        if (!decision.Allowed)
        {
            await WriteAuditEntryAsync(empresa, AuditOutcome.Denied, decision.Reason, cancellationToken);
            return Result.Failure(new Error(
                "Organizacion.Empresas.NoAutorizado", decision.Reason, ErrorType.Forbidden));
        }

        empresa.Desactivar();
        repository.Update(empresa);

        await WriteAuditEntryAsync(empresa, AuditOutcome.Success, null, cancellationToken);

        return Result.Success();
    }

    private async Task WriteAuditEntryAsync(
        Empresa empresa, AuditOutcome outcome, string? reason, CancellationToken cancellationToken)
    {
        var auditRequest = new AuditEntryRequest(
            actor: actorContext.GetCurrentActor(),
            tenantId: empresa.TenantId == Guid.Empty ? null : empresa.TenantId,
            action: "organizacion.empresas.desactivar",
            resource: new AuditResource("organizacion.empresas", empresa.Id.ToString()),
            outcome: outcome,
            reason: reason,
            metadata: new Dictionary<string, string?> { ["razonSocial"] = empresa.RazonSocial });
        _ = await auditWriter.WriteAsync(auditRequest, cancellationToken);
    }
}
