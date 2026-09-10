using System.Reflection;
using System.Text;
using BitCode.Framework.Tools.Migrations.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BitCode.Framework.Tools.Migrations;

/// <summary>
/// CLI para el ciclo de vida de migraciones en BitCode (Fase 8, F8-07):
/// validación expand-and-contract, consulta de estado, forward rollout y rollback ensayado.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        var command = args[0].ToLowerInvariant();
        var cmdArgs = args.Skip(1).ToArray();

        try
        {
            return command switch
            {
                "validate" => RunValidate(cmdArgs),
                "status" => await RunStatusAsync(cmdArgs),
                "migrate" => await RunMigrateAsync(cmdArgs),
                "rollback" => await RunRollbackAsync(cmdArgs),
                "script" => RunScript(cmdArgs),
                _ => HandleUnknownCommand(command)
            };
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR FATAL]: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"  Detalle: {ex.InnerException.Message}");
            }
            Console.ResetColor();
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("======================================================================");
        Console.WriteLine(" BitCode Migrations CLI — Tooling de Ciclo de Vida y Rollout (F8-07)  ");
        Console.WriteLine("======================================================================");
        Console.WriteLine("Uso: BitCode.Migrations <comando> [opciones]\n");
        Console.WriteLine("Comandos disponibles:");
        Console.WriteLine("  validate   Analiza migraciones contra reglas Expand-and-Contract (zero-downtime).");
        Console.WriteLine("  status     Muestra las migraciones aplicadas y pendientes en la base de datos.");
        Console.WriteLine("  migrate    Aplica migraciones pendientes (Forward rollout) tras validar.");
        Console.WriteLine("  rollback   Revierte la base de datos a una migración previa (Rollback ensayado).");
        Console.WriteLine("  script     Genera un script SQL idempotente de migración para DBAs/pipelines.\n");
        Console.WriteLine("Opciones comunes:");
        Console.WriteLine("  --connection-string <cs>   Cadena de conexión SQL Server (o env: ConnectionStrings__Default)");
        Console.WriteLine("  --assembly <path>          Ruta al ensamblado (.dll) que contiene el DbContext y migraciones");
        Console.WriteLine("  --context <name>           Nombre de la clase DbContext a utilizar");
        Console.WriteLine("  --target <name>            Nombre de la migración objetivo (o '0' para revertir todo)");
        Console.WriteLine("  --allow-destructive        Permite operaciones destructivas (fase Contract autorizada)");
        Console.WriteLine("  --output <file>            Archivo de salida para el script SQL generado");
    }

    private static int HandleUnknownCommand(string command)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Comando desconocido: '{command}'");
        Console.ResetColor();
        PrintUsage();
        return 1;
    }

    private static int RunValidate(string[] args)
    {
        var assemblyPath = GetOption(args, "--assembly");
        var allowDestructive = args.Contains("--allow-destructive");

        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: El parámetro --assembly <ruta> es obligatorio para el comando validate.");
            Console.ResetColor();
            return 1;
        }

        var fullPath = Path.GetFullPath(assemblyPath);
        if (!File.Exists(fullPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: No se encontró el ensamblado en '{fullPath}'.");
            Console.ResetColor();
            return 1;
        }

        var asm = Assembly.LoadFrom(fullPath);
        var migrationTypes = asm.GetTypes()
            .Where(t => !t.IsAbstract && typeof(Migration).IsAssignableFrom(t))
            .ToList();

        if (migrationTypes.Count == 0)
        {
            Console.WriteLine($"No se encontraron clases de migración en '{asm.GetName().Name}'.");
            return 0;
        }

        Console.WriteLine($"Analizando {migrationTypes.Count} migración(es) en '{asm.GetName().Name}'...");
        Console.WriteLine($"Modo Expand-and-Contract: {(allowDestructive ? "PERMISIVO (--allow-destructive activo)" : "ESTRICTO (cero downtime)")}\n");

        var validator = new MigrationValidator(allowDestructive);
        var totalErrors = 0;
        var totalWarnings = 0;

        foreach (var type in migrationTypes)
        {
            var migrationInstance = Activator.CreateInstance(type) as Migration;
            if (migrationInstance == null) continue;

            var result = validator.ValidateMigration(migrationInstance);

            if (result.Violations.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  [OK] {result.MigrationName}: Conforme con Expand-and-Contract.");
                Console.ResetColor();
            }
            else
            {
                foreach (var v in result.Violations)
                {
                    if (v.Severity == RuleSeverity.Error)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"  [ERROR] {v.MigrationName} -> {v.OperationType} ({v.Target}): {v.Message}");
                        Console.WriteLine($"          Remediación: {v.Remediation}");
                        Console.ResetColor();
                        totalErrors++;
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"  [WARN]  {v.MigrationName} -> {v.OperationType} ({v.Target}): {v.Message}");
                        Console.WriteLine($"          Sugerencia: {v.Remediation}");
                        Console.ResetColor();
                        totalWarnings++;
                    }
                }
            }
        }

        Console.WriteLine("\n--------------------------------------------------");
        Console.WriteLine($"Resultado: {totalErrors} error(es), {totalWarnings} advertencia(s).");

        if (totalErrors > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Fallo de validación: Se detectaron operaciones que violan la regla Expand-and-Contract.");
            Console.ResetColor();
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Validación exitosa: Todas las migraciones son compatibles con despliegue zero-downtime.");
        Console.ResetColor();
        return 0;
    }

    private static async Task<int> RunStatusAsync(string[] args)
    {
        var (context, _, _) = CreateDbContext(args);
        await using (context)
        {
            var runner = new MigrationRunner(context);
            var status = await runner.GetStatusAsync();

            Console.WriteLine($"DbContext: {status.DbContextName}");
            Console.WriteLine($"Base de datos: {status.DatabaseName}");
            Console.WriteLine($"Total migraciones definidas: {status.AllMigrations.Count}");
            Console.WriteLine($"Migraciones aplicadas:       {status.AppliedMigrations.Count}");
            Console.WriteLine($"Migraciones pendientes:      {status.PendingMigrations.Count}");
            Console.WriteLine($"Última aplicada:             {status.LatestAppliedMigration ?? "(ninguna)"}\n");

            if (status.PendingMigrations.Count > 0)
            {
                Console.WriteLine("Migraciones pendientes por aplicar (Forward):");
                foreach (var m in status.PendingMigrations)
                {
                    Console.WriteLine($"  - {m}");
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("La base de datos está completamente actualizada al último esquema.");
                Console.ResetColor();
            }

            return 0;
        }
    }

    private static async Task<int> RunMigrateAsync(string[] args)
    {
        var targetMigration = GetOption(args, "--target");
        var allowDestructive = args.Contains("--allow-destructive");
        var (context, asm, _) = CreateDbContext(args);

        await using (context)
        {
            // Paso 1: Validación previa obligatoria
            var validator = new MigrationValidator(allowDestructive);
            var migrationTypes = asm.GetTypes()
                .Where(t => !t.IsAbstract && typeof(Migration).IsAssignableFrom(t))
                .ToList();

            var errorsFound = false;
            foreach (var type in migrationTypes)
            {
                if (Activator.CreateInstance(type) is Migration m)
                {
                    var valRes = validator.ValidateMigration(m);
                    if (!valRes.IsValid)
                    {
                        errorsFound = true;
                        foreach (var v in valRes.Violations.Where(x => x.Severity == RuleSeverity.Error))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"[VALIDACIÓN BLOQUEANTE] {v.MigrationName}: {v.Message}");
                            Console.ResetColor();
                        }
                    }
                }
            }

            if (errorsFound && !allowDestructive)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Despliegue abortado: Se encontraron operaciones destructivas. Use --allow-destructive si cuenta con aprobación explícita.");
                Console.ResetColor();
                return 1;
            }

            // Paso 2: Ejecución del forward rollout
            Console.WriteLine($"Iniciando Forward Rollout para {context.GetType().Name}...");
            var runner = new MigrationRunner(context);
            var newlyApplied = await runner.MigrateForwardAsync(targetMigration);

            if (newlyApplied.Count == 0)
            {
                Console.WriteLine("No se aplicó ninguna migración nueva (la base de datos ya estaba al nivel objetivo).");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Se aplicaron exitosamente {newlyApplied.Count} migración(es):");
                foreach (var m in newlyApplied)
                {
                    Console.WriteLine($"  + {m}");
                }
                Console.ResetColor();
            }

            return 0;
        }
    }

    private static async Task<int> RunRollbackAsync(string[] args)
    {
        var targetMigration = GetOption(args, "--target");
        if (string.IsNullOrWhiteSpace(targetMigration))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Debe especificar la migración objetivo para el rollback con --target <nombre_migracion> (o '0' para revertir todas).");
            Console.ResetColor();
            return 1;
        }

        var (context, _, _) = CreateDbContext(args);
        await using (context)
        {
            Console.WriteLine($"Iniciando Rollback hacia '{targetMigration}' en {context.GetType().Name}...");
            var runner = new MigrationRunner(context);
            var rolledBack = await runner.RollbackAsync(targetMigration);

            if (rolledBack.Count == 0)
            {
                Console.WriteLine("No se revirtió ninguna migración (la base de datos ya se encontraba en el nivel objetivo o anterior).");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Rollback ensayado completado con éxito. Se revirtieron {rolledBack.Count} migración(es):");
                foreach (var m in rolledBack)
                {
                    Console.WriteLine($"  - {m}");
                }
                Console.ResetColor();
            }

            return 0;
        }
    }

    private static int RunScript(string[] args)
    {
        var from = GetOption(args, "--from");
        var to = GetOption(args, "--to");
        var outputFile = GetOption(args, "--output");
        var (context, _, _) = CreateDbContext(args);

        using (context)
        {
            var generator = new MigrationScriptGenerator(context);
            var sql = generator.GenerateIdempotentScript(from, to);

            if (!string.IsNullOrWhiteSpace(outputFile))
            {
                File.WriteAllText(outputFile, sql, Encoding.UTF8);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Script SQL idempotente generado con éxito en '{outputFile}'.");
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine(sql);
            }

            return 0;
        }
    }

    private static (DbContext Context, Assembly Asm, Type ContextType) CreateDbContext(string[] args)
    {
        var assemblyPath = GetOption(args, "--assembly");
        var contextName = GetOption(args, "--context");
        var connectionString = GetOption(args, "--connection-string")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? Environment.GetEnvironmentVariable("ConnectionString");

        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            throw new InvalidOperationException("El parámetro --assembly <ruta> es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Debe especificar una cadena de conexión con --connection-string o mediante la variable de entorno ConnectionStrings__Default.");
        }

        var fullPath = Path.GetFullPath(assemblyPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"No se encontró el ensamblado en '{fullPath}'.");
        }

        var asm = Assembly.LoadFrom(fullPath);
        var dbContextTypes = asm.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DbContext).IsAssignableFrom(t))
            .ToList();

        Type? targetType = null;
        if (!string.IsNullOrWhiteSpace(contextName))
        {
            targetType = dbContextTypes.FirstOrDefault(t => t.Name.Equals(contextName, StringComparison.OrdinalIgnoreCase));
            if (targetType == null)
            {
                throw new InvalidOperationException($"No se encontró ningún DbContext llamado '{contextName}' en '{asm.GetName().Name}'.");
            }
        }
        else
        {
            if (dbContextTypes.Count == 1)
            {
                targetType = dbContextTypes[0];
            }
            else if (dbContextTypes.Count == 0)
            {
                throw new InvalidOperationException($"No se encontraron tipos derivados de DbContext en '{asm.GetName().Name}'.");
            }
            else
            {
                throw new InvalidOperationException($"Se encontraron múltiples DbContexts ({string.Join(", ", dbContextTypes.Select(t => t.Name))}). Especifique uno con --context <nombre>.");
            }
        }

        // Buscar si existe IDesignTimeDbContextFactory<T>
        var factoryInterface = typeof(IDesignTimeDbContextFactory<>).MakeGenericType(targetType);
        var factoryType = asm.GetTypes().FirstOrDefault(t => !t.IsAbstract && factoryInterface.IsAssignableFrom(t));

        if (factoryType != null)
        {
            var factoryInstance = Activator.CreateInstance(factoryType);
            var createMethod = factoryType.GetMethod("CreateDbContext", [typeof(string[])]);
            if (createMethod != null && factoryInstance != null)
            {
                var contextFromFactory = createMethod.Invoke(factoryInstance, [args]) as DbContext;
                if (contextFromFactory != null)
                {
                    return (contextFromFactory, asm, targetType);
                }
            }
        }

        // Instanciación directa con DbContextOptionsBuilder
        var optionsBuilderType = typeof(DbContextOptionsBuilder<>).MakeGenericType(targetType);
        var optionsBuilder = (DbContextOptionsBuilder)Activator.CreateInstance(optionsBuilderType)!;
        optionsBuilder.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.MigrationsAssembly(asm.GetName().Name);
        });

        // Intentar constructor con DbContextOptions<T> o DbContextOptions
        var options = optionsBuilder.Options;
        var ctor = targetType.GetConstructor([options.GetType()])
            ?? targetType.GetConstructor([typeof(DbContextOptions)]);

        if (ctor != null)
        {
            var instance = (DbContext)ctor.Invoke([options]);
            return (instance, asm, targetType);
        }

        // Fallback a constructor sin parámetros si existiese
        var defaultCtor = targetType.GetConstructor(Type.EmptyTypes);
        if (defaultCtor != null)
        {
            var instance = (DbContext)defaultCtor.Invoke(null);
            return (instance, asm, targetType);
        }

        throw new InvalidOperationException($"No se pudo instanciar el DbContext '{targetType.Name}'. Asegúrese de que tenga un constructor que reciba DbContextOptions o implemente IDesignTimeDbContextFactory<{targetType.Name}>.");
    }

    private static string? GetOption(string[] args, string optionName)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(optionName, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
