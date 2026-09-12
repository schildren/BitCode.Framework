using BitCode.Framework.Platform.ImportExport.Exportacion;
using Microsoft.EntityFrameworkCore;

namespace Sample.ImportExport.Api.Clientes;

/// <summary>
/// Implementación de referencia de <see cref="IExportDataSource"/> para el tipo de exportación
/// "clientes" -- simétrica a <see cref="ClientesImportRowHandler"/>. <see cref="Cliente.Id"/> es la
/// posición estable usada para paginar (orden determinístico requerido por el <c>remarks</c> de
/// <see cref="IExportDataSource.ObtenerPaginaAsync"/>).
/// </summary>
public sealed class ClientesExportDataSource(SampleClientesDbContext dbContext) : IExportDataSource
{
    public string TipoExportacion => "clientes";

    public IReadOnlyList<string> Columnas => ["Nombre", "Email"];

    public async Task<ExportPageResult> ObtenerPaginaAsync(
        Guid tenantId, string? filtroJson, int offset, int tamanoLote, CancellationToken cancellationToken)
    {
        var total = await dbContext.Clientes.CountAsync(c => c.TenantId == tenantId, cancellationToken);

        var pagina = await dbContext.Clientes
            .Where(c => c.TenantId == tenantId)
            .OrderBy(c => c.Id)
            .Skip(offset)
            .Take(tamanoLote)
            .Select(c => new List<string> { c.Nombre, c.Email })
            .ToListAsync(cancellationToken);

        var filas = pagina.Select(f => (IReadOnlyList<string>)f).ToList();
        var hayMasFilas = offset + filas.Count < total;

        return new ExportPageResult(filas, hayMasFilas, total);
    }
}
