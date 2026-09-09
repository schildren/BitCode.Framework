using BitCode.Framework.Shared.Domain.MultiTenancy;

namespace Sample.ImportExport.Api.Clientes;

/// <summary>
/// Modelo de negocio DEL HOST (nunca del framework) usado por
/// <see cref="ClientesImportRowHandler"/>/<see cref="ClientesExportDataSource"/> para demostrar,
/// end-to-end, cómo un consumidor real implementa los puntos de extensión de
/// <c>BitCode.Platform.ImportExport</c> (Fase 6, módulo 10). Deliberadamente simple (dos columnas) --
/// el foco de este módulo es el motor de lotes/progreso/errores/reanudación, no el modelado de un
/// dominio de "clientes" real.
/// </summary>
public sealed class Cliente
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string Nombre { get; set; } = string.Empty;

    /// <summary>Clave natural de upsert -- ver el <c>remarks</c> de
    /// <c>BitCode.Framework.Platform.ImportExport.Importacion.IImportRowHandler</c>: reprocesar la misma
    /// fila tras una caída a mitad de un chunk debe ser idempotente.</summary>
    public string Email { get; set; } = string.Empty;
}
