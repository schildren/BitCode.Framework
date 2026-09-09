namespace BitCode.Gateway.Tests.Integration;

/// <summary>
/// Colección xUnit para serializar la ejecución de los tests de integración del Gateway que
/// configuran <c>Program.cs</c> vía variables de entorno globales al proceso (<c>Jwt__*</c>,
/// <c>ReverseProxy__*</c>, <c>Regional__*</c> -- ver comentario en
/// <see cref="GatewayIntegrationTests.InitializeAsync"/> sobre por qué es necesario usar variables de
/// entorno en vez de <c>ConfigureAppConfiguration</c>). Sin esta colección, xUnit ejecuta clases de
/// test distintas en paralelo por defecto: dos clases mutando las mismas variables de entorno del
/// proceso al mismo tiempo (p. ej. <c>Jwt__SecretKey</c> con valores distintos) produce fallos
/// intermitentes no relacionados con ningún bug real del Gateway.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class GatewayEnvironmentVariableCollection
{
    public const string Name = "Gateway environment variables (sequential)";
}
