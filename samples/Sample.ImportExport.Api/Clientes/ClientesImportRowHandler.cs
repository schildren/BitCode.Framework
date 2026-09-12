using BitCode.Framework.Platform.ImportExport.Importacion;
using BitCode.Framework.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace Sample.ImportExport.Api.Clientes;

/// <summary>
/// Implementación de referencia de <see cref="IImportRowHandler"/> para el tipo de importación
/// "clientes" -- demuestra, end-to-end, cómo un consumidor real de <c>BitCode.Platform.ImportExport</c>
/// (Fase 6, módulo 10) aplica cada fila validada a su propio modelo de negocio. Valida el formato de
/// <c>Email</c> (más allá de "no está vacío", que ya valida el job antes de llamar acá) y hace UPSERT por
/// <c>Email</c> (clave natural, ver <see cref="Cliente.Email"/>) -- idempotente ante un reproceso tras una
/// caída a mitad de un chunk (ver el <c>remarks</c> de <see cref="IImportRowHandler"/>).
/// </summary>
public sealed class ClientesImportRowHandler(SampleClientesDbContext dbContext) : IImportRowHandler
{
    public string TipoImportacion => "clientes";

    public IReadOnlyList<string> ColumnasRequeridas => ["Nombre", "Email"];

    public async Task<Result> ProcesarFilaAsync(Guid tenantId, IReadOnlyDictionary<string, string> fila, CancellationToken cancellationToken)
    {
        var nombre = fila["Nombre"];
        var email = fila["Email"];

        if (!email.Contains('@'))
        {
            return Result.Failure(Error.Validation("Clientes.EmailInvalido", $"'{email}' no es un email válido."));
        }

        var existente = await dbContext.Clientes
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Email == email, cancellationToken);

        if (existente is null)
        {
            dbContext.Clientes.Add(new Cliente { Id = Guid.NewGuid(), TenantId = tenantId, Nombre = nombre, Email = email });
        }
        else
        {
            existente.Nombre = nombre;
        }

        // Persistencia propia, fuera del pipeline CQRS del framework -- ver el remarks de
        // IImportRowHandler: este SaveChangesAsync explícito es legítimo acá porque esta clase no es un
        // IRequestHandler de MediatR dentro de TransactionBehavior.
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
