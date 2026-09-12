using System.Text.Json;
using BitCode.Framework.Tools.Migrations.Core.DataReconciliation;

namespace BitCode.Framework.Tools.Migrations.Formatters;

/// <summary>
/// Presenta el resultado del comando <c>reconcile</c> (Fase 9, F9-08) en consola o como JSON estructurado
/// para pipelines/auditoría.
/// </summary>
public static class ReconciliationReportFormatter
{
    public static void RenderConsole(ReconciliationReport report)
    {
        Console.WriteLine();
        Console.WriteLine("======================================================================");
        Console.WriteLine(" Reconciliación de datos (F9-08)                                      ");
        Console.WriteLine("======================================================================");

        foreach (var table in report.Tables)
        {
            if (table.Error is not null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  [ERROR] {table.TableName}: {table.Error}");
                Console.ResetColor();
                continue;
            }

            if (table.IsReconciled)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  [OK] {table.TableName}: {table.SourceCount} fila(s), hash {ShortHash(table.SourceAggregateHash)} — conciliado.");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  [DISCREPANCIA] {table.TableName}: origen={table.SourceCount} fila(s) (hash {ShortHash(table.SourceAggregateHash)}), destino={table.TargetCount} fila(s) (hash {ShortHash(table.TargetAggregateHash)})");
                if (table.FirstMismatch is not null)
                {
                    Console.WriteLine($"      Primera fila distinta: índice={table.FirstMismatch.RowIndex}, clave=[{table.FirstMismatch.KeyDescription}]");
                    Console.WriteLine($"      hash fila origen={table.FirstMismatch.SourceRowHash}");
                    Console.WriteLine($"      hash fila destino={table.FirstMismatch.TargetRowHash}");
                }
                Console.ResetColor();
            }
        }

        Console.WriteLine("----------------------------------------------------------------------");
        if (report.IsFullyReconciled)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Resultado: CONCILIADO — conteos y hashes coinciden en todas las tablas.");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Resultado: DISCREPANCIA ENCONTRADA — revise el detalle de las tablas marcadas arriba.");
        }

        Console.ResetColor();
    }

    public static string ToJson(ReconciliationReport report) =>
        JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });

    private static string ShortHash(string hash) => hash.Length > 12 ? hash[..12] + "..." : hash;
}
