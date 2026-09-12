using System.Reflection;
using MyApp.Modules;

namespace MyApp.Modules.Tests;

/// <summary>
/// Prueba de arquitectura/límites del módulo: un módulo generado por "dotnet new bitcode-module" solo
/// puede depender del framework transversal (<c>Shared.*</c>) y de librerías de terceros de propósito
/// general -- NUNCA de otro módulo de negocio (ni <c>BitCode.Framework.Platform.*</c> ni un ensamblado de
/// otro bounded context). Si dos módulos necesitan comunicarse, es a través de un evento de integración
/// (Outbox/Inbox) o de la API HTTP pública del otro módulo, nunca por <c>ProjectReference</c> directo (ver
/// docs/architecture-principles.md, sección 1 "Modularidad", y docs/convenciones.md).
///
/// Esta prueba verifica esa regla sobre el ensamblado YA COMPILADO del módulo (no sobre el código fuente):
/// enumera los ensamblados referenciados por <see cref="ModuleNameDbContext"/> (representativo del módulo,
/// vive en el ensamblado raíz) y falla si aparece cualquier referencia fuera de la lista explícita de
/// prefijos permitidos.
/// </summary>
public sealed class ModuleBoundaryTests
{
    // El AssemblyName real de los proyectos Shared.* del framework es "Shared.Kernel", "Shared.Domain",
    // etc (no "BitCode.Framework.Shared.*" -- ese es el namespace de sus tipos, no el nombre del
    // ensamblado, ver <ProjectReference> en ModuleName.csproj) -- "Shared" es, por lo tanto, el único
    // prefijo de ensamblado de negocio permitido acá.
    private static readonly string[] AllowedAssemblyNamePrefixes =
    [
        "System",
        "Microsoft",
        "netstandard",
        "mscorlib",
        "MediatR",
        "FluentValidation",
        "Asp.Versioning",
        "Shared", // único límite permitido: el framework transversal (Shared.*), nunca otro módulo
    ];

    [Fact]
    public void ModuleAssembly_SoloReferenciaSharedYLibreriasDeTercerosPermitidas()
    {
        var moduleAssembly = typeof(ModuleNameDbContext).Assembly;

        var referencedAssemblyNames = moduleAssembly.GetReferencedAssemblies()
            .Select(assemblyName => assemblyName.Name ?? string.Empty)
            .ToList();

        var disallowed = referencedAssemblyNames
            .Where(name => !AllowedAssemblyNamePrefixes.Any(prefix =>
                name.Equals(prefix, StringComparison.Ordinal) ||
                name.StartsWith(prefix + ".", StringComparison.Ordinal)))
            .ToList();

        Assert.True(
            disallowed.Count == 0,
            $"El módulo referencia ensamblados fuera de sus límites permitidos: {string.Join(", ", disallowed)}. " +
            "Un módulo de negocio nunca referencia otro módulo de negocio (ni Platform.* ni otro bounded " +
            "context) -- si necesitás comunicarte con otro módulo, usá un evento de integración (Outbox/Inbox) " +
            "o su API HTTP pública, no un ProjectReference directo.");
    }

    [Fact]
    public void ModuleAssembly_NoReferenciaNingunModuloDePlataformaNiDeOtroBoundedContext()
    {
        var moduleAssembly = typeof(ModuleNameDbContext).Assembly;

        var referencedAssemblyNames = moduleAssembly.GetReferencedAssemblies()
            .Select(assemblyName => assemblyName.Name ?? string.Empty);

        Assert.DoesNotContain(
            referencedAssemblyNames,
            // "BitCode.Platform.*" es el AssemblyName real de los módulos de plataforma del framework
            // (ver src/Platform/BitCode.Platform.*/*.csproj); se incluye también el prefijo de namespace
            // por si algún consumidor nombra su propio ensamblado siguiendo ese patrón.
            name => name.StartsWith("BitCode.Platform.", StringComparison.Ordinal) ||
                    name.StartsWith("BitCode.Framework.Platform.", StringComparison.Ordinal));
    }
}
