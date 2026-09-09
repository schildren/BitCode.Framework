using BitCode.Framework.Shared.Domain.MultiTenancy;
using FluentAssertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.Redis;

namespace BitCode.Framework.Shared.Infrastructure.Caching.Tests.Integration;

/// <summary>
/// F5-06 (Fase 5 — Disaster Recovery y multi-región, <c>docs/cache-regional-fase5.md</c>): demuestra
/// el criterio de aceptación "Cache no condiciona recuperación" — un fallo TOTAL del cache regional
/// (Valkey/Redis como L2 de <see cref="HybridCache"/>) no debe impedir leer ni "escribir" (poblar) el
/// dato real, aunque sea con más latencia, y nunca debe lanzar una excepción sin controlar hacia el
/// código de negocio que dependería de que el cache esté disponible para poder operar.
///
/// Complementa (no duplica) <see cref="CacheUnavailabilityCapacityTests"/> (F4-14, Fase 4): esa
/// prueba ya demuestra, con el mismo contenedor Redis real detenido, que el HEALTH CHECK "redis"
/// refleja Unhealthy sin lanzar una excepción sin controlar. Esta prueba demuestra la propiedad
/// distinta y más fuerte que exige F5-06: que el CAMINO DE NEGOCIO real
/// (<see cref="HybridCache.GetOrCreateAsync{TState,T}"/>, la única API que usa
/// <see cref="ITenantAwareCache"/>/<see cref="TenantAwareCache"/>) sigue devolviendo el dato correcto
/// —viniendo de la fuente de verdad (el <c>factory</c>, que en producción es la consulta real a SQL
/// Server/el servicio de dominio)— cuando Redis (L2) está completamente caído, sin que la ausencia
/// de L2 se propague como una falla observable para quien llama.
/// </summary>
public class CacheUnavailabilityDoesNotBlockSourceOfTruthTests
{
    [Fact]
    public async Task GetOrCreateAsync_ConRedisCompletamenteCaido_DevuelveElDatoDeLaFuenteDeVerdad_SinLanzarExcepcion()
    {
        var redisContainer = new RedisBuilder().Build();
        await redisContainer.StartAsync();

        try
        {
            var connectionString = redisContainer.GetConnectionString();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Caching:RedisConnectionString"] = connectionString,
                })
                .Build();

            // 1) Con Redis real arriba: se puebla una clave para simular que ya fue calentada en
            //    algún momento anterior (equivalente al warming tras un failover, sección 2 de
            //    docs/cache-regional-fase5.md).
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSharedCaching(configuration);
            await using var provider = services.BuildServiceProvider();
            var cache = provider.GetRequiredService<HybridCache>();

            const string keyYaCacheada = "producto:ya-calentada";
            var valorPrevio = await cache.GetOrCreateAsync(
                keyYaCacheada, _ => ValueTask.FromResult("valor-fuente-de-verdad-original"));
            valorPrevio.Should().Be("valor-fuente-de-verdad-original");

            // 2) Se detiene el contenedor real de Redis -- caída TOTAL del cache regional, no un
            //    timeout parcial ni una excepción simulada.
            await redisContainer.StopAsync();

            // 3) Simula el escenario más exigente: un proceso NUEVO (L1 en memoria vacía, como una
            //    instancia recién levantada tras un failover regional, sección 2.1 del documento de
            //    diseño) que todavía apunta a la connection string de Redis, ahora inalcanzable.
            var servicesTrasCaida = new ServiceCollection();
            servicesTrasCaida.AddLogging();
            servicesTrasCaida.AddSharedCaching(configuration);
            await using var providerTrasCaida = servicesTrasCaida.BuildServiceProvider();
            var cacheTrasCaida = providerTrasCaida.GetRequiredService<HybridCache>();

            // 3a) LECTURA de una clave que ni siquiera está en la L1 de este proceso nuevo (fue
            //     "escrita" en el paso 1 por otro HybridCache/L1 distinto): con L2 inalcanzable, el
            //     único camino posible para obtener el dato es el factory -- la fuente de verdad real.
            var factoryEjecutado = false;
            Func<Task> leerConL2Caido = async () =>
            {
                var valor = await cacheTrasCaida.GetOrCreateAsync(keyYaCacheada, _ =>
                {
                    factoryEjecutado = true;
                    return ValueTask.FromResult("valor-fuente-de-verdad-releido-tras-caida-de-redis");
                });

                valor.Should().Be("valor-fuente-de-verdad-releido-tras-caida-de-redis",
                    "con Redis (L2) totalmente caído y sin la entrada en la L1 de este proceso, el " +
                    "único dato correcto posible es el que devuelve la fuente de verdad (el factory)");
            };

            await leerConL2Caido.Should().NotThrowAsync(
                "un fallo total del cache regional (L2) nunca debe propagarse como una excepción sin " +
                "controlar hacia el código de negocio que solo quiere leer el dato -- debe degradar a " +
                "leer de la fuente de verdad, con más latencia, nunca bloquear la operación");
            factoryEjecutado.Should().BeTrue(
                "sin L2 disponible y sin la entrada en L1, el dato SOLO puede venir de la fuente de verdad");

            // 3b) "ESCRITURA" (poblar una clave nueva, el único mecanismo de escritura de
            //     HybridCache): tampoco debe fallar ni bloquear aunque no pueda persistir en L2 --
            //     debe devolver igual el valor real recién calculado.
            const string keyNueva = "producto:nunca-antes-cacheada";
            var factoryDeEscrituraEjecutado = false;
            Func<Task> escribirConL2Caido = async () =>
            {
                var valor = await cacheTrasCaida.GetOrCreateAsync(keyNueva, _ =>
                {
                    factoryDeEscrituraEjecutado = true;
                    return ValueTask.FromResult("valor-recien-escrito-en-la-fuente-de-verdad");
                });

                valor.Should().Be("valor-recien-escrito-en-la-fuente-de-verdad");
            };

            await escribirConL2Caido.Should().NotThrowAsync(
                "poblar una clave nueva (equivalente a una escritura seguida de warming del cache) " +
                "tampoco debe depender de que L2 esté disponible para completarse con éxito");
            factoryDeEscrituraEjecutado.Should().BeTrue();

            // 4) HALLAZGO REAL de esta prueba: a diferencia de GetOrCreateAsync (pasos 3a/3b),
            //    HybridCache.RemoveAsync SÍ propaga sin controlar la excepción de conectividad de L2
            //    (StackExchange.Redis.RedisConnectionException) -- no cae de vuelta a un no-op. Esto
            //    documentaba, antes de esta tarea, una ruta de código real donde una invalidación
            //    (p. ej. disparada tras confirmar un commit) SÍ dependía de que el cache regional
            //    estuviera disponible -- lo que el criterio de aceptación de F5-06 prohíbe
            //    explícitamente. Se deja esta aserción para no perder la evidencia del hallazgo.
            Func<Task> invalidarConHybridCacheCrudoConL2Caido = async () =>
                await cacheTrasCaida.RemoveAsync(keyYaCacheada);

            await invalidarConHybridCacheCrudoConL2Caido.Should().ThrowAsync<Exception>(
                "hallazgo real: HybridCache.RemoveAsync no absorbe la falla de conectividad de L2 " +
                "como sí lo hace GetOrCreateAsync -- por eso TenantAwareCache.RemoveAsync (verificado " +
                "a continuación) agrega su propio manejo best-effort en vez de delegar sin más");

            // 5) La corrección de esta tarea: ITenantAwareCache.RemoveAsync (el punto de entrada
            //    recomendado para invalidar cache de negocio, ver ITenantAwareCache) SÍ debe absorber
            //    la misma falla de L2 caído y continuar sin propagarla -- best-effort, con el TTL
            //    corto de cada entrada (sección 3.3 del documento de diseño) como red de seguridad
            //    ante este caso y ante eventos de invalidación cross-región perdidos.
            // FakeTenantContext (doble ya usado en TenantAwareCacheTests, no se inventa uno nuevo) con
            // multi-tenancy deshabilitado: esta prueba no necesita un TenantId resuelto, solo verificar
            // el comportamiento de RemoveAsync frente a un L2 caído.
            var tenantAwareCacheTrasCaida = new TenantAwareCache(
                cacheTrasCaida,
                new FakeTenantContext(tenantId: null, isMultiTenancyEnabled: false),
                NullLogger<TenantAwareCache>.Instance);

            Func<Task> invalidarConTenantAwareCacheConL2Caido = async () =>
                await tenantAwareCacheTrasCaida.RemoveAsync(keyYaCacheada);

            await invalidarConTenantAwareCacheConL2Caido.Should().NotThrowAsync(
                "una invalidación best-effort que no puede alcanzar el cache regional caído no debe " +
                "propagar la falla hacia el flujo de negocio que la disparó (p. ej. tras confirmar un " +
                "commit) -- el TTL corto es la red de seguridad, no una excepción sin controlar");
        }
        finally
        {
            await redisContainer.DisposeAsync();
        }
    }
}
