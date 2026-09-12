using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Redis;

namespace BitCode.Framework.Shared.Infrastructure.Caching.Tests.Integration;

/// <summary>
/// F4-14 (Fase 4 — Capacity tests): "Cache no disponible" es una de las 10 pruebas obligatorias
/// listadas en la sección "Pruebas obligatorias" de la Fase 4 del Plan Maestro. Reproduce, con Redis
/// real (Testcontainers, no un mock), la misma secuencia que F4-04 ya validó en ejecución para SQL
/// Server (<c>docs/politica-manifiestos-kubernetes.md</c> sección 5-bis: dependencia disponible →
/// arriba → caída → readiness cae sin lanzar excepción sin controlar → dependencia se recupera →
/// readiness vuelve a subir), pero para el check "redis" registrado por
/// <see cref="CachingServiceCollectionExtensions.AddSharedCaching"/> — que <c>samples/Sample.Api</c>
/// no ejercita hoy (no llama <c>AddSharedCaching</c>, ver <c>InfrastructureModule.cs</c>), así que
/// esta prueba construye su propio host de referencia mínimo con el mismo cableado que documenta
/// <c>docs/guia-health-checks.md</c> (<c>AddSharedCaching</c> + <c>HealthCheckService</c>), en vez de
/// levantar el proceso HTTP completo de Sample.Api solo para este propósito.
///
/// No usa <see cref="RedisContainerFixture"/> (compartida entre tests de esta clase de colección)
/// porque este test necesita control exclusivo del ciclo de vida del contenedor (pararlo y
/// reiniciarlo a mitad de la prueba) — un contenedor propio, no compartido con otros tests.
/// </summary>
public class CacheUnavailabilityCapacityTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public async Task RedisHealthCheck_ReflejaCaidaYRecuperacionDelContenedorReal_SinLanzarExcepcionSinControlar()
    {
        var redisContainer = new RedisBuilder().Build();
        await redisContainer.StartAsync();
        output.WriteLine($"ConnectionString inicial: {redisContainer.GetConnectionString()}");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Caching:RedisConnectionString"] = redisContainer.GetConnectionString(),
                })
                .Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSharedCaching(configuration);
            await using var provider = services.BuildServiceProvider();

            var healthCheckService = provider.GetRequiredService<HealthCheckService>();

            // 1) Redis real arriba: el check "redis" (tag "ready") debe reportar Healthy -- mismo
            //    comportamiento que un readinessProbe real vería como 200.
            var reportConDependenciaArriba = await healthCheckService.CheckHealthAsync(
                registration => registration.Tags.Contains("ready"));
            reportConDependenciaArriba.Status.Should().Be(HealthStatus.Healthy,
                "con el contenedor de Redis real corriendo, el check debe reportar Healthy");

            // 2) Se detiene el contenedor real (equivalente a que el Pod de Redis/el servicio gestionado
            //    deje de responder) -- StopAsync detiene el proceso del contenedor sin destruirlo.
            await redisContainer.StopAsync();

            var reportConDependenciaCaida = await healthCheckService.CheckHealthAsync(
                registration => registration.Tags.Contains("ready"));
            reportConDependenciaCaida.Status.Should().Be(HealthStatus.Unhealthy,
                "con Redis real detenido, el check debe reportar Unhealthy (equivalente a que un " +
                "readinessProbe real reciba 503 y saque la instancia del Service, SIN reiniciarla -- " +
                "'redis' nunca está en /health/live)");
            reportConDependenciaCaida.Entries["redis"].Exception.Should().NotBeNull(
                "RedisDistributedCacheHealthCheck debe capturar la excepción de conexión real, no dejarla propagar sin controlar");

            // 3) Se reinicia el contenedor -- HALLAZGO REAL de esta prueba, documentado explícitamente
            //    (no ocultado): en este entorno (Testcontainers .NET 4.x sobre Docker Desktop/Windows),
            //    StartAsync() tras StopAsync() NO reutiliza el mismo puerto de host publicado
            //    (confirmado con el log de ambas ConnectionString: cambia, p. ej. 127.0.0.1:57033 ->
            //    127.0.0.1:57044) -- el multiplexor de StackExchange.Redis ya construido (apuntando al
            //    puerto viejo) nunca puede reconectar porque el proceso que escuchaba ese puerto ya no
            //    es el mismo. Verificado con 90s de polling real: el check queda en Unhealthy de forma
            //    indefinida contra el puerto viejo (ver la salida capturada de esta prueba).
            //
            //    Esto es una particularidad del **entorno de prueba con Testcontainers** (contenedor
            //    efímero con puerto de host aleatorio en cada (re)inicio), NO del código del framework
            //    ni representativo de un `Service` de Kubernetes real: un `Service` (`ClusterIP`)
            //    resuelve siempre la MISMA dirección DNS estable
            //    (`redis.<namespace>.svc.cluster.local:6379`) sin importar a qué Pod/IP se enruta
            //    detrás -- StackExchange.Redis SÍ reconecta solo contra la misma dirección cuando el
            //    Pod de Redis vuelve (comportamiento estándar y ampliamente documentado del cliente,
            //    no verificado en esta prueba porque requeriría un Service real). Ver el informe F4-14
            //    (`docs/informe-capacity-tests-f4-14.md`) para el detalle completo de esta distinción y
            //    por qué queda como brecha explícita, no una aserción falsa.
            //
            //    Lo que SÍ queda demostrado en esta prueba, con evidencia real: (a) el check refleja
            //    Unhealthy sin lanzar ninguna excepción sin controlar hacia HealthCheckService (paso 2),
            //    y (b) una conexión NUEVA contra la dirección donde el servicio recuperado realmente
            //    escucha ahora sí lo ve Healthy de inmediato -- lo más parecido a "un pod nuevo /una
            //    verificación fresca contra el servicio recuperado" que se puede demostrar sin un
            //    Service de Kubernetes real detrás.
            await redisContainer.StartAsync();
            output.WriteLine($"ConnectionString tras reinicio: {redisContainer.GetConnectionString()}");

            var reportContraMultiplexorViejo = await healthCheckService.CheckHealthAsync(
                registration => registration.Tags.Contains("ready"));
            reportContraMultiplexorViejo.Status.Should().Be(HealthStatus.Unhealthy,
                "hallazgo real de esta prueba: el multiplexor ya construido apunta al puerto de host " +
                "anterior, que Testcontainers no reutiliza tras StopAsync()+StartAsync() en este " +
                "entorno -- ver el comentario de esta sección y docs/informe-capacity-tests-f4-14.md");

            var servicesFrescos = new ServiceCollection();
            servicesFrescos.AddLogging();
            servicesFrescos.AddSharedCaching(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Caching:RedisConnectionString"] = redisContainer.GetConnectionString(),
                })
                .Build());
            await using var providerFresco = servicesFrescos.BuildServiceProvider();

            var reportConexionFresca = await providerFresco.GetRequiredService<HealthCheckService>()
                .CheckHealthAsync(registration => registration.Tags.Contains("ready"));
            reportConexionFresca.Status.Should().Be(HealthStatus.Healthy,
                "una conexión nueva contra la dirección donde Redis real volvió a escuchar debe " +
                "reportar Healthy de inmediato -- confirma que el servicio en sí se recuperó y que el " +
                "hallazgo del bloque anterior es del multiplexor viejo, no de Redis");
        }
        finally
        {
            await redisContainer.DisposeAsync();
        }
    }
}
