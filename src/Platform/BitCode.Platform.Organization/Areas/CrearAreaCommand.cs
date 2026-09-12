using BitCode.Framework.Platform.Organization.Sucursales;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.Organization.Areas;

/// <summary>
/// Alta de área bajo una sucursal existente -- corte simplificado (CRUD básico, ver
/// <c>docs/guia-organization.md</c>, sección "Alcance del primer corte"): sin desactivación, sin
/// eventos de dominio propios, sin <see cref="BitCode.Framework.Shared.Domain.Idempotency.IIdempotencyStore"/>
/// tampoco -- SÍ implementa <see cref="IIdempotentCommand"/> (F1-22), igual que el resto de las
/// mutaciones expuestas por API de este módulo, para no dejar un hueco de idempotencia en un POST.
/// Si se pasa <see cref="ParentAreaId"/>, valida que exista y pertenezca a la MISMA sucursal (consistencia
/// mínima de jerarquía; no valida ciclos -- pendiente explícito, ver la guía).
/// </summary>
internal sealed record CrearAreaCommand(Guid SucursalId, string Nombre, Guid? ParentAreaId)
    : ICommand<Guid>, IIdempotentCommand;

internal sealed class CrearAreaCommandValidator : AbstractValidator<CrearAreaCommand>
{
    public CrearAreaCommandValidator()
    {
        RuleFor(c => c.SucursalId).NotEmpty();
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(200);
    }
}

internal sealed class CrearAreaCommandHandler(
    IRepository<Area, Guid> areaRepository,
    IReadRepository<Sucursal, Guid> sucursalRepository)
    : IRequestHandler<CrearAreaCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CrearAreaCommand request, CancellationToken cancellationToken)
    {
        var sucursal = await sucursalRepository.GetByIdAsync(request.SucursalId, cancellationToken);
        if (sucursal is null)
        {
            return Result.Failure<Guid>(Error.NotFound(
                "Organizacion.Sucursales.NoEncontrada", $"No existe la sucursal {request.SucursalId}."));
        }

        if (request.ParentAreaId is { } parentAreaId)
        {
            var parentArea = await areaRepository.GetByIdAsync(parentAreaId, cancellationToken);
            if (parentArea is null || parentArea.SucursalId != request.SucursalId)
            {
                return Result.Failure<Guid>(new Error(
                    "Organizacion.Areas.AreaPadreInvalida",
                    "El área padre indicada no existe o no pertenece a la misma sucursal.",
                    ErrorType.Validation));
            }
        }

        var area = new Area(Guid.NewGuid(), request.SucursalId, request.Nombre, request.ParentAreaId);
        await areaRepository.AddAsync(area, cancellationToken);

        return area.Id;
    }
}
