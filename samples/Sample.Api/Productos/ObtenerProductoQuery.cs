using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace Sample.Api.Productos;

public record ObtenerProductoQuery(Guid Id) : IQuery<ProductoResponse>;

public record ProductoResponse(Guid Id, string Nombre, decimal Precio);

public class ObtenerProductoQueryHandler(IReadRepository<Producto, Guid> repository)
    : IRequestHandler<ObtenerProductoQuery, Result<ProductoResponse>>
{
    public async Task<Result<ProductoResponse>> Handle(ObtenerProductoQuery request, CancellationToken cancellationToken)
    {
        var producto = await repository.GetByIdAsync(request.Id, cancellationToken);

        return producto is null
            ? Result.Failure<ProductoResponse>(Error.NotFound("Producto.NoEncontrado", $"No existe el producto {request.Id}."))
            : new ProductoResponse(producto.Id, producto.Nombre, producto.Precio);
    }
}
