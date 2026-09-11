using System.Reflection;
using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using FluentAssertions;
using MediatR;
using Xunit;

namespace BitCode.Framework.Architecture.Tests.GoldenPaths;

/// <summary>
/// F8-10: Pruebas de humo estructurales para verificar la completitud y conformidad
/// de los 5 Golden Paths (CRUD/CQRS, Workflow, Events, Documents, Integration Hub)
/// documentados en docs/golden-paths.md.
/// </summary>
public class GoldenPathSmokeTests
{
    [Fact]
    public void GoldenPath_Crud_Contratos_Y_Estructura_Existen_Y_Compilan()
    {
        // 1. Debe existir contrato ICommand, IQuery, Result, IRepository, IReadRepository
        typeof(BitCode.Framework.Shared.Application.Messaging.ICommand).Should().NotBeNull();
        typeof(BitCode.Framework.Shared.Application.Messaging.IQuery<>).Should().NotBeNull();
        typeof(Result).Should().NotBeNull();
        typeof(Result<>).Should().NotBeNull();
        typeof(IRepository<,>).Should().NotBeNull();
        typeof(IReadRepository<,>).Should().NotBeNull();

        // 2. La implementación canónica de muestra en Sample.Api debe compilar y exponer endpoints
        var sampleApiAssembly = typeof(Sample.Api.Productos.CrearProductoCommand).Assembly;
        var sampleCommands = sampleApiAssembly.GetTypes()
            .Where(t => t.Name.EndsWith("Command") && !t.IsAbstract)
            .ToList();

        sampleCommands.Should().NotBeEmpty("Sample.Api debe contener comandos que representen el Golden Path CRUD.");
    }

    [Fact]
    public void GoldenPath_Workflow_Contratos_Y_Servicios_Existen()
    {
        var workflowAssembly = typeof(BitCode.Framework.Platform.Workflow.WorkflowDbContext).Assembly;
        
        // Verifica que existan tipos clave del modelo de Workflow
        var types = workflowAssembly.GetTypes();
        types.Should().Contain(t => t.Name == "WorkflowDefinition", "WorkflowDefinition debe existir en Platform.Workflow.");
        types.Should().Contain(t => t.Name == "WorkflowVersion", "WorkflowVersion debe existir en Platform.Workflow.");
        types.Should().Contain(t => t.Name == "WorkflowInstance", "WorkflowInstance debe existir en Platform.Workflow.");
        types.Should().Contain(t => t.Name == "WorkflowTask", "WorkflowTask debe existir en Platform.Workflow.");

        // Verifica la extensión de configuración
        var extensions = types.Where(t => t.Name.Contains("Workflow") && t.Name.EndsWith("Extensions")).ToList();
        extensions.Should().NotBeEmpty("Platform.Workflow debe exponer métodos de extensión de configuración.");
    }

    [Fact]
    public void GoldenPath_Events_Contratos_Y_Mecanismo_Outbox_Inbox_Existen()
    {
        // 1. Contratos de eventos
        typeof(IIntegrationEvent).Should().NotBeNull();
        typeof(IntegrationEvent).Should().NotBeNull();
        typeof(IEventPublisher).Should().NotBeNull();
        typeof(IEventConsumer<>).Should().NotBeNull();

        // 2. Entidades Outbox e Inbox residen en Shared.Domain
        typeof(BitCode.Framework.Shared.Domain.Outbox.OutboxMessage).Should().NotBeNull();
        typeof(BitCode.Framework.Shared.Domain.Inbox.InboxMessage).Should().NotBeNull();

        // 3. Procesador y Relay de Outbox residen en Shared.Infrastructure.Persistence
        var persistenceAssembly = typeof(BitCode.Framework.Shared.Infrastructure.Persistence.UnitOfWork).Assembly;
        persistenceAssembly.GetTypes().Should().Contain(t => t.Name.Contains("OutboxBatchProcessor"), "OutboxBatchProcessor debe existir en Persistence.");
        persistenceAssembly.GetTypes().Should().Contain(t => t.Name.Contains("OutboxPublisherBackgroundService"), "OutboxPublisherBackgroundService debe existir en Persistence.");

        // 3. Prueba de referencia F3-13 en Sample.Eventing
        var eventingAssembly = typeof(Sample.Eventing.Pedidos.Pedido).Assembly;
        eventingAssembly.Should().NotBeNull();
    }

    [Fact]
    public void GoldenPath_Documents_Contratos_Y_Almacenamiento_Existen()
    {
        var documentsAssembly = typeof(BitCode.Framework.Platform.Documents.DocumentsDbContext).Assembly;
        var types = documentsAssembly.GetTypes();

        types.Should().Contain(t => t.Name == "Documento", "Entidad Documento debe existir.");
        types.Should().Contain(t => t.Name == "DocumentoVersion", "Entidad DocumentoVersion debe existir.");
        types.Should().Contain(t => t.Name == "IDocumentBlobStore", "Contrato de almacenamiento IDocumentBlobStore debe existir.");
        types.Should().Contain(t => t.Name == "IAntivirusScanner", "Contrato de antivirus IAntivirusScanner debe existir.");
    }

    [Fact]
    public void GoldenPath_IntegrationHub_Contratos_Y_Mapping_Existen()
    {
        var integrationAssembly = typeof(BitCode.Framework.Platform.IntegrationHub.IntegrationHubDbContext).Assembly;
        var types = integrationAssembly.GetTypes();

        types.Should().Contain(t => t.Name == "IntegrationConnector", "IntegrationConnector debe existir.");
        types.Should().Contain(t => t.Name == "IntegrationFieldMapping", "IntegrationFieldMapping debe existir.");
        types.Should().Contain(t => t.Name == "IntegrationRequest", "IntegrationRequest debe existir.");
        types.Should().Contain(t => t.Name == "IntegrationRequestLog", "IntegrationRequestLog debe existir.");
    }
}
