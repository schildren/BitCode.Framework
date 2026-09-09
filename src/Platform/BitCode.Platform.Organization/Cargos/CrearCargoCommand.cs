using BitCode.Framework.Platform.Organization.Areas;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.Organization.Cargos;

/// <summary>
/// Alta de cargo bajo un área existente -- último nivel de la jerarquía, mismo alcance simplificado
/// que <c>CrearAreaCommand</c> (ver <c>docs/guia-organization.md</c>).
/// </summary>
internal sealed record CrearCargoCommand(Guid AreaId, string Nombre) : ICommand<Guid>, IIdempotentCommand;

internal sealed class CrearCargoCommandValidator : AbstractValidator<CrearCargoCommand>
{
    public CrearCargoCommandValidator()
    {
        RuleFor(c => c.AreaId).NotEmpty();
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(200);
    }
}

internal sealed class CrearCargoCommandHandler(
    IRepository<Cargo, Guid> cargoRepository,
    IReadRepository<Area, Guid> areaRepository)
    : IRequestHandler<CrearCargoCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CrearCargoCommand request, CancellationToken cancellationToken)
    {
        var area = await areaRepository.GetByIdAsync(request.AreaId, cancellationToken);
        if (area is null)
        {
            return Result.Failure<Guid>(Error.NotFound(
                "Organizacion.Areas.NoEncontrada", $"No existe el área {request.AreaId}."));
        }

        var cargo = new Cargo(Guid.NewGuid(), request.AreaId, request.Nombre);
        await cargoRepository.AddAsync(cargo, cancellationToken);

        return cargo.Id;
    }
}
