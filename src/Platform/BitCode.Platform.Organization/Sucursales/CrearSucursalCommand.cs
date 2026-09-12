using BitCode.Framework.Platform.Organization.Actors;
using BitCode.Framework.Platform.Organization.Empresas;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.Organization.Sucursales;

/// <summary>
/// Alta de sucursal bajo una empresa existente y activa (Fase 6, módulo Organization). Implementa
/// <see cref="IIdempotentCommand"/> (F1-22), mismo criterio que <c>CrearEmpresaCommand</c>.
/// </summary>
internal sealed record CrearSucursalCommand(Guid EmpresaId, string Nombre, string? Direccion)
    : ICommand<Guid>, IIdempotentCommand;

internal sealed class CrearSucursalCommandValidator : AbstractValidator<CrearSucursalCommand>
{
    public CrearSucursalCommandValidator()
    {
        RuleFor(c => c.EmpresaId).NotEmpty();
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Direccion).MaximumLength(400);
    }
}

internal sealed class CrearSucursalCommandHandler(
    IRepository<Sucursal, Guid> sucursalRepository,
    IReadRepository<Empresa, Guid> empresaRepository,
    IAuditWriter auditWriter,
    IOrganizationActorContext actorContext)
    : IRequestHandler<CrearSucursalCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CrearSucursalCommand request, CancellationToken cancellationToken)
    {
        var empresa = await empresaRepository.GetByIdAsync(request.EmpresaId, cancellationToken);
        if (empresa is null)
        {
            return Result.Failure<Guid>(Error.NotFound(
                "Organizacion.Empresas.NoEncontrada", $"No existe la empresa {request.EmpresaId}."));
        }

        if (!empresa.Activa)
        {
            return Result.Failure<Guid>(new Error(
                "Organizacion.Sucursales.EmpresaInactiva",
                "No se puede crear una sucursal para una empresa desactivada.",
                ErrorType.Validation));
        }

        var sucursal = new Sucursal(Guid.NewGuid(), request.EmpresaId, request.Nombre, request.Direccion);
        await sucursalRepository.AddAsync(sucursal, cancellationToken);

        var auditRequest = new AuditEntryRequest(
            actor: actorContext.GetCurrentActor(),
            tenantId: null,
            action: "organizacion.sucursales.crear",
            resource: new AuditResource("organizacion.sucursales", sucursal.Id.ToString()),
            outcome: AuditOutcome.Success,
            reason: null,
            metadata: new Dictionary<string, string?> { ["empresaId"] = empresa.Id.ToString(), ["nombre"] = sucursal.Nombre });
        _ = await auditWriter.WriteAsync(auditRequest, cancellationToken);

        return sucursal.Id;
    }
}
