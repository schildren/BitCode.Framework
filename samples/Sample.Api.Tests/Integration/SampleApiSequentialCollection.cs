namespace Sample.Api.Tests.Integration;

/// <summary>
/// Fuerza ejecución secuencial entre las clases de test que, como <c>ProductosEndpointsIntegrationTests</c>
/// (Fase 8) y <c>HealthCheckEndpointsIntegrationTests</c> (F1-25), configuran
/// <c>ConnectionStrings__Default</c> vía variable de entorno de PROCESO antes de construir su propio
/// <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/>
/// (<c>Program.cs</c> lee <c>builder.Configuration</c> de forma síncrona en <c>AddModules</c>, antes de
/// que <c>ConfigureAppConfiguration</c> tenga oportunidad de interceptar). xUnit ejecuta clases de
/// distintas colecciones en paralelo por defecto -- sin este agrupamiento, dos factories
/// construyéndose al mismo tiempo pisan mutuamente la variable de entorno compartida del proceso.
/// </summary>
[CollectionDefinition(Name)]
public class SampleApiSequentialCollection
{
    public const string Name = "Sample.Api sequential (variable de entorno compartida)";
}
