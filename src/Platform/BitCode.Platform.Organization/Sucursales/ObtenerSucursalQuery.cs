using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Organization.Sucursales;

internal sealed record ObtenerSucursalQuery(Guid Id) : IQuery<SucursalResponse>;

internal sealed class ObtenerSucursalQueryHandler(IReadRepository<Sucursal, Guid> repository)
    : IRequestHandler<ObtenerSucursalQuery, Result<SucursalResponse>>
{
    public async Task<Result<SucursalResponse>> Handle(ObtenerSucursalQuery request, CancellationToken cancellationToken)
    {
        var sucursal = await repository.GetByIdAsync(request.Id, cancellationToken);

        return sucursal is null
            ? Result.Failure<SucursalResponse>(
                Error.NotFound("Organizacion.Sucursales.NoEncontrada", $"No existe la sucursal {request.Id}."))
            : new SucursalResponse(sucursal.Id, sucursal.EmpresaId, sucursal.Nombre, sucursal.Direccion, sucursal.Activa);
    }
}
