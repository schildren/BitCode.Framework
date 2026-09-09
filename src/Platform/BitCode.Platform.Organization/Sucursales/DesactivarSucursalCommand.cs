using BitCode.Framework.Platform.Organization.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.Organization.Sucursales;

/// <summary>
/// Desactiva una sucursal individual -- alcance más acotado que <c>DesactivarEmpresaCommand</c> (una
/// sola sucursal, no la empresa completa con todas sus sucursales), por eso usa un permiso RBAC propio
/// (<see cref="OrganizationPermissions.SucursalesDesactivar"/>) sin la capa ABAC adicional que sí
/// exige la desactivación de una empresa completa.
/// </summary>
internal sealed record DesactivarSucursalCommand(Guid Id) : ICommand, IIdempotentCommand;

internal sealed class DesactivarSucursalCommandValidator : AbstractValidator<DesactivarSucursalCommand>
{
    public DesactivarSucursalCommandValidator() => RuleFor(c => c.Id).NotEmpty();
}

internal sealed class DesactivarSucursalCommandHandler(
    IRepository<Sucursal, Guid> repository,
    IAuditWriter auditWriter,
    IOrganizationActorContext actorContext)
    : IRequestHandler<DesactivarSucursalCommand, Result>
{
    public async Task<Result> Handle(DesactivarSucursalCommand request, CancellationToken cancellationToken)
    {
        var sucursal = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (sucursal is null)
        {
            return Result.Failure(Error.NotFound(
                "Organizacion.Sucursales.NoEncontrada", $"No existe la sucursal {request.Id}."));
        }

        sucursal.Desactivar();
        repository.Update(sucursal);

        var auditRequest = new AuditEntryRequest(
            actor: actorContext.GetCurrentActor(),
            tenantId: sucursal.TenantId == Guid.Empty ? null : sucursal.TenantId,
            action: "organizacion.sucursales.desactivar",
            resource: new AuditResource("organizacion.sucursales", sucursal.Id.ToString()),
            outcome: AuditOutcome.Success,
            reason: null,
            metadata: new Dictionary<string, string?> { ["nombre"] = sucursal.Nombre });
        _ = await auditWriter.WriteAsync(auditRequest, cancellationToken);

        return Result.Success();
    }
}
