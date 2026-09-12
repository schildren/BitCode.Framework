using System.Linq;
using System.Reflection;
using FluentAssertions;
using Xunit;

namespace BitCode.Framework.Architecture.Tests.Layers;

/// <summary>
/// Fase 9 (F9-02, "Contract boundary" -- <c>docs/plan-maestro-bitcode-ia.md</c>, backlog de Fase 9): el
/// hallazgo que motivó esta tarea fue que <c>BitCode.Platform.TaskInbox</c> y
/// <c>BitCode.Platform.Notifications</c> (y, según se confirmó al ejecutar F9-02, también
/// <c>BitCode.Platform.Reporting</c>) tenían un <c>ProjectReference</c> DIRECTO contra
/// <c>BitCode.Platform.Workflow</c> solo para reutilizar el tipo .NET de sus eventos de integración, en
/// vez de depender de un ensamblado de contratos separado (ver
/// <c>docs/adr/0020-fase9-seleccion-piloto-extraccion-microservicio.md</c>). Ese acoplamiento de
/// COMPILACIÓN es exactamente lo que impediría extraer un módulo como servicio independiente sin romper
/// a sus consumidores (regla dura de Fase 9). Este test NO depende de que se conozca de antemano cuáles
/// dos o tres módulos tienen hoy el problema: enumera TODOS los ensamblados de módulos de plataforma
/// conocidos por este proyecto de test y falla si CUALQUIERA de ellos referencia el ensamblado de OTRO
/// módulo de plataforma que no sea explícitamente un ensamblado de "solo contratos" (sufijo
/// <c>.Contracts</c>) -- así la regla queda enforced en CI para cualquier módulo futuro, no solo
/// corregida una vez para los dos/tres módulos conocidos hoy.
/// </summary>
/// <remarks>
/// Deliberadamente NO se usa <c>NetArchTest.Rules</c> acá (a diferencia de
/// <see cref="PlatformModuleBoundaryTests"/>): NetArchTest evalúa por namespace/nombre de tipo, y el
/// ensamblado de contratos de Workflow (<c>BitCode.Platform.Workflow.Contracts</c>) conserva
/// DELIBERADAMENTE el mismo namespace que tenía en <c>BitCode.Platform.Workflow</c>
/// (<c>BitCode.Framework.Platform.Workflow.Instancias</c>/<c>.Definiciones</c>, ver ese proyecto para el
/// porqué) -- una regla basada en namespace no podría distinguir "referencia solo el contrato" de
/// "referencia el módulo completo". La identidad real que sí distingue ambos casos es el ENSAMBLADO
/// (<see cref="Assembly.GetReferencedAssemblies"/>), no el namespace de los tipos que contiene.
/// </remarks>
public class ContractBoundaryTests
{
    /// <summary>
    /// Todos los ensamblados de los 12 módulos de la Plataforma Funcional Empresarial (Fase 6),
    /// identificados por el nombre simple del ensamblado (igual al nombre del proyecto/carpeta bajo
    /// <c>src/Platform</c>, ver cada <c>.csproj</c>: ninguno fija <c>&lt;AssemblyName&gt;</c> distinto).
    /// La lista debe cubrir los 12 módulos completos -- este test es intencionalmente más amplio que
    /// <see cref="PlatformModuleBoundaryTests"/> (que solo detecta dependencias de TIPO contra el
    /// <c>DbContext</c> específico de otro módulo, no un <c>ProjectReference</c> a nivel de ensamblado
    /// contra cualquier otro tipo del módulo) -- ambos tests se complementan, no se sustituyen. Un
    /// módulo nuevo de plataforma que no aparezca acá queda fuera de esta verificación aunque agregue un
    /// <c>ProjectReference</c> prohibido, así que cualquier módulo agregado a
    /// <c>docs/plan-maestro-bitcode-ia.md</c> (Fase 6) debe sumarse también acá.
    /// </summary>
    private static readonly (string AssemblyName, Assembly Assembly)[] PlatformModuleAssemblies =
    [
        ("BitCode.Platform.Identity", typeof(BitCode.Framework.Platform.Identity.IdentityAdministrationDbContext).Assembly),
        ("BitCode.Platform.Organization", typeof(BitCode.Framework.Platform.Organization.OrganizationDbContext).Assembly),
        ("BitCode.Platform.Catalogs", typeof(BitCode.Framework.Platform.Catalogs.CatalogsDbContext).Assembly),
        ("BitCode.Platform.FeatureManagement", typeof(BitCode.Framework.Platform.FeatureManagement.FeatureManagementDbContext).Assembly),
        ("BitCode.Platform.Documents", typeof(BitCode.Framework.Platform.Documents.DocumentsDbContext).Assembly),
        ("BitCode.Platform.Workflow", typeof(BitCode.Framework.Platform.Workflow.WorkflowDbContext).Assembly),
        ("BitCode.Platform.TaskInbox", typeof(BitCode.Framework.Platform.TaskInbox.TaskInboxDbContext).Assembly),
        ("BitCode.Platform.Notifications", typeof(BitCode.Framework.Platform.Notifications.NotificationsDbContext).Assembly),
        ("BitCode.Platform.Reporting", typeof(BitCode.Framework.Platform.Reporting.ReportingDbContext).Assembly),
        ("BitCode.Platform.ImportExport", typeof(BitCode.Framework.Platform.ImportExport.ImportExportDbContext).Assembly),
        ("BitCode.Platform.Dashboard", typeof(BitCode.Framework.Platform.Dashboard.DashboardDbContext).Assembly),
        ("BitCode.Platform.IntegrationHub", typeof(BitCode.Framework.Platform.IntegrationHub.IntegrationHubDbContext).Assembly),
    ];

    [Fact]
    public void Platform_Modules_Must_Not_Reference_Other_Platform_Module_Assemblies_Directly()
    {
        // Regla: F9-02 -- ningún módulo de plataforma puede tener un ProjectReference (que en el
        // ensamblado compilado se ve como AssemblyName referenciado) contra el ensamblado COMPLETO de
        // otro módulo de plataforma. Si necesita reutilizar sus eventos de integración, debe depender de
        // un ensamblado de contratos separado (por convención, sufijo ".Contracts" -- ver
        // BitCode.Platform.Workflow.Contracts), nunca del módulo dueño de la lógica de negocio.
        var moduleNames = PlatformModuleAssemblies.Select(m => m.AssemblyName).ToHashSet();

        foreach (var (name, assembly) in PlatformModuleAssemblies)
        {
            var referencedAssemblyNames = assembly.GetReferencedAssemblies()
                .Select(a => a.Name)
                .Where(n => n is not null)
                .Cast<string>()
                .ToArray();

            var forbiddenDirectReferences = referencedAssemblyNames
                .Where(referenced => referenced != name && moduleNames.Contains(referenced))
                .ToArray();

            forbiddenDirectReferences.Should().BeEmpty(
                $"El módulo de plataforma '{name}' no debe tener un ProjectReference directo al " +
                $"ensamblado completo de otro módulo de plataforma (encontrado: " +
                $"{string.Join(", ", forbiddenDirectReferences)}). Si necesita reutilizar un evento de " +
                "integración de ese módulo, debe depender de un ensamblado de SOLO contratos (sufijo " +
                "'.Contracts', ver BitCode.Platform.Workflow.Contracts y " +
                "docs/adr/0020-fase9-seleccion-piloto-extraccion-microservicio.md, F9-02).");
        }
    }

    [Fact]
    public void Workflow_Consumers_Must_Depend_Only_On_The_Workflow_Contracts_Assembly()
    {
        // Caso concreto que motivó F9-02: verifica explícitamente que los tres consumidores reales de
        // eventos de Workflow (TaskInbox, Notifications, Reporting) SÍ referencian el ensamblado de
        // contratos (deben poder resolver los tipos de evento), pero NINGUNO referencia el ensamblado
        // completo BitCode.Platform.Workflow.
        var contractsAssemblyName = typeof(BitCode.Framework.Platform.Workflow.Instancias.TareaAsignadaIntegrationEvent).Assembly.GetName().Name;
        var workflowAssemblyName = typeof(BitCode.Framework.Platform.Workflow.WorkflowDbContext).Assembly.GetName().Name;

        contractsAssemblyName.Should().Be("BitCode.Platform.Workflow.Contracts");
        workflowAssemblyName.Should().Be("BitCode.Platform.Workflow");

        var consumers = new (string Name, Assembly Assembly)[]
        {
            ("BitCode.Platform.TaskInbox", typeof(BitCode.Framework.Platform.TaskInbox.TaskInboxDbContext).Assembly),
            ("BitCode.Platform.Notifications", typeof(BitCode.Framework.Platform.Notifications.NotificationsDbContext).Assembly),
            ("BitCode.Platform.Reporting", typeof(BitCode.Framework.Platform.Reporting.ReportingDbContext).Assembly),
        };

        foreach (var (name, assembly) in consumers)
        {
            var referenced = assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();

            referenced.Should().Contain(contractsAssemblyName,
                $"'{name}' consume eventos de integración de Workflow y debe referenciar el ensamblado " +
                "de contratos (BitCode.Platform.Workflow.Contracts).");

            referenced.Should().NotContain(workflowAssemblyName,
                $"'{name}' no debe referenciar el ensamblado completo de BitCode.Platform.Workflow " +
                "(motor de estados, WorkflowDbContext, comandos/queries internos) -- F9-02.");
        }
    }
}
