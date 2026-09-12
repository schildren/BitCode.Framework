using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace Sample.Api.Productos;

/// <summary>
/// F1-27: versión 2 de "obtener producto", mapeada a <c>GET /api/v2/productos/{id}</c> junto a
/// <see cref="ObtenerProductoQuery"/> (v1, <c>GET /api/v1/productos/{id}</c>), que sigue existiendo
/// sin cambios -- esta es la demostración concreta del criterio de aceptación de F1-27 ("versiones
/// coexistentes"): v2 no reemplaza ni modifica el contrato de v1, agrega un query/response propio.
/// Diferencia real (no cosmética) entre v1 y v2: <see cref="ProductoResponseV2"/> agrega
/// <c>CreadoEnUtc</c>, un campo que v1 nunca expuso -- si en cambio hubiera sido aditivo/opcional
/// con default razonable, la política de versionado (docs/politica-versionado.md, sección 4) exige
/// agregarlo directo al endpoint existente sin nueva versión; acá se modela como v2 a propósito para
/// ejercitar el mecanismo de versionado, no porque el campo en sí exija una versión mayor.
/// </summary>
public record ObtenerProductoV2Query(Guid Id) : IQuery<ProductoResponseV2>;

public record ProductoResponseV2(Guid Id, string Nombre, decimal Precio, DateTime CreadoEnUtc);

public class ObtenerProductoV2QueryHandler(IReadRepository<Producto, Guid> repository)
    : IRequestHandler<ObtenerProductoV2Query, Result<ProductoResponseV2>>
{
    public async Task<Result<ProductoResponseV2>> Handle(ObtenerProductoV2Query request, CancellationToken cancellationToken)
    {
        var producto = await repository.GetByIdAsync(request.Id, cancellationToken);

        return producto is null
            ? Result.Failure<ProductoResponseV2>(Error.NotFound("Producto.NoEncontrado", $"No existe el producto {request.Id}."))
            : new ProductoResponseV2(producto.Id, producto.Nombre, producto.Precio, producto.CreatedAtUtc);
    }
}
