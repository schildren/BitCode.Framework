using BitCode.Framework.Shared.Infrastructure.Security.Oidc.Bff;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Oidc.Bff;

/// <summary>
/// F2-03, criterio de aceptación "Tokens no quedan expuestos al navegador": estas pruebas cubren la
/// mitad "server-side" de esa garantía -- que la sesión efectivamente guarda los tokens fuera del
/// alcance del cliente y que el contenido cacheado está protegido (no en texto plano). La otra mitad
/// (que el navegador solo recibe un identificador opaco en la cookie) se cubre a nivel de endpoint HTTP
/// en Shared.Infrastructure.Web.Tests.
/// </summary>
public class DistributedCacheBffSessionStoreTests
{
    private static readonly BffSession SampleSession = new(
        AccessToken: "access-token-123",
        RefreshToken: "refresh-token-123",
        IdToken: "id-token-123",
        AccessTokenExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(5),
        Claims: new Dictionary<string, string> { ["sub"] = "user-1", ["name"] = "Ada Lovelace" });

    private static (IDistributedCache Cache, IDataProtectionProvider Protector) CreateInfrastructure()
    {
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        services.AddDataProtection();
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IDistributedCache>(), provider.GetRequiredService<IDataProtectionProvider>());
    }

    [Fact]
    public async Task CreateAsync_ThenGetAsync_RoundTripsTheSession()
    {
        var (cache, protector) = CreateInfrastructure();
        var store = new DistributedCacheBffSessionStore(cache, protector);

        var sessionId = await store.CreateAsync(SampleSession, TimeSpan.FromMinutes(30));
        var retrieved = await store.GetAsync(sessionId);

        // BeEquivalentTo, no Be: BffSession.Claims es IReadOnlyDictionary -- la instancia deserializada
        // del JSON nunca es la misma referencia que la original, así que la igualdad estructural del
        // record generada por el compilador (que delega en EqualityComparer<T>.Default para ese campo)
        // compararía por referencia y fallaría aunque el contenido sea idéntico.
        retrieved.Should().BeEquivalentTo(SampleSession);
    }

    [Fact]
    public async Task CreateAsync_GeneratesADifferentOpaqueIdentifierEachTime()
    {
        var (cache, protector) = CreateInfrastructure();
        var store = new DistributedCacheBffSessionStore(cache, protector);

        var firstId = await store.CreateAsync(SampleSession, TimeSpan.FromMinutes(30));
        var secondId = await store.CreateAsync(SampleSession, TimeSpan.FromMinutes(30));

        firstId.Should().NotBe(secondId);
        // El identificador no debe contener el token en texto plano -- es opaco, no una codificación
        // reversible del contenido de la sesión.
        firstId.Should().NotContain(SampleSession.AccessToken);
    }

    [Fact]
    public async Task GetAsync_WithUnknownSessionId_ReturnsNull()
    {
        var (cache, protector) = CreateInfrastructure();
        var store = new DistributedCacheBffSessionStore(cache, protector);

        var retrieved = await store.GetAsync("no-existe");

        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_RevokesTheSession()
    {
        var (cache, protector) = CreateInfrastructure();
        var store = new DistributedCacheBffSessionStore(cache, protector);
        var sessionId = await store.CreateAsync(SampleSession, TimeSpan.FromMinutes(30));

        await store.RemoveAsync(sessionId);
        var retrieved = await store.GetAsync(sessionId);

        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_OnAnAlreadyAbsentSession_DoesNotThrow()
    {
        var (cache, protector) = CreateInfrastructure();
        var store = new DistributedCacheBffSessionStore(cache, protector);

        var act = async () => await store.RemoveAsync("nunca-existio");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RenewAsync_ReplacesTheContentWithoutChangingTheSessionId()
    {
        var (cache, protector) = CreateInfrastructure();
        var store = new DistributedCacheBffSessionStore(cache, protector);
        var sessionId = await store.CreateAsync(SampleSession, TimeSpan.FromMinutes(30));

        var renewedSession = SampleSession with { AccessToken = "access-token-renewed" };
        await store.RenewAsync(sessionId, renewedSession, TimeSpan.FromMinutes(30));
        var retrieved = await store.GetAsync(sessionId);

        retrieved.Should().BeEquivalentTo(renewedSession);
    }

    [Fact]
    public async Task GetAsync_WithTamperedCacheEntry_ReturnsNullInsteadOfThrowing()
    {
        var (cache, protector) = CreateInfrastructure();
        var store = new DistributedCacheBffSessionStore(cache, protector);
        var sessionId = await store.CreateAsync(SampleSession, TimeSpan.FromMinutes(30));

        // Simula un operador de la caché compartida (Redis/Valkey) intentando alterar el valor
        // guardado: sin la clave de Data Protection correcta, Unprotect debe fallar de forma controlada.
        await cache.SetAsync($"bc-bff-session:{sessionId}", "esto-no-esta-protegido"u8.ToArray());

        var retrieved = await store.GetAsync(sessionId);

        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task RevokeAllForSubjectAsync_RevokesEveryActiveSessionOfThatSubject()
    {
        // F2-06, "cerrar sesión en todos los dispositivos": dos sesiones distintas (dos "dispositivos")
        // del mismo sujeto -- ambas deben quedar revocadas por una sola llamada, sin conocer de antemano
        // sus sessionId individuales.
        var (cache, protector) = CreateInfrastructure();
        var store = new DistributedCacheBffSessionStore(cache, protector);
        var firstSessionId = await store.CreateAsync(SampleSession, TimeSpan.FromMinutes(30));
        var secondSessionId = await store.CreateAsync(SampleSession, TimeSpan.FromMinutes(30));

        await store.RevokeAllForSubjectAsync("user-1");

        (await store.GetAsync(firstSessionId)).Should().BeNull();
        (await store.GetAsync(secondSessionId)).Should().BeNull();
    }

    [Fact]
    public async Task RevokeAllForSubjectAsync_DoesNotAffectSessionsOfOtherSubjects()
    {
        var (cache, protector) = CreateInfrastructure();
        var store = new DistributedCacheBffSessionStore(cache, protector);
        var ownSessionId = await store.CreateAsync(SampleSession, TimeSpan.FromMinutes(30));
        var otherSubjectSession = SampleSession with { Claims = new Dictionary<string, string> { ["sub"] = "user-2" } };
        var otherSessionId = await store.CreateAsync(otherSubjectSession, TimeSpan.FromMinutes(30));

        await store.RevokeAllForSubjectAsync("user-1");

        (await store.GetAsync(ownSessionId)).Should().BeNull();
        (await store.GetAsync(otherSessionId)).Should().NotBeNull("revocar las sesiones de un sujeto nunca debe afectar las de otro");
    }

    [Fact]
    public async Task RevokeAllForSubjectAsync_ForASubjectWithoutActiveSessions_DoesNotThrow()
    {
        var (cache, protector) = CreateInfrastructure();
        var store = new DistributedCacheBffSessionStore(cache, protector);

        var act = async () => await store.RevokeAllForSubjectAsync("sin-sesiones-activas");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RemoveAsync_ThenRevokeAllForSubjectAsync_DoesNotFailOnTheAlreadyRemovedSession()
    {
        // El logout individual (RemoveAsync) ya limpia la entrada del índice por sujeto (best-effort) --
        // una revocación posterior de "todas las sesiones" del mismo sujeto no debe fallar ni intentar
        // revocar una sesión que ya no existe.
        var (cache, protector) = CreateInfrastructure();
        var store = new DistributedCacheBffSessionStore(cache, protector);
        var sessionId = await store.CreateAsync(SampleSession, TimeSpan.FromMinutes(30));
        await store.RemoveAsync(sessionId);

        var act = async () => await store.RevokeAllForSubjectAsync("user-1");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RenewAsync_KeepsTheSubjectIndexUpToDate()
    {
        // RenewAsync reescribe el contenido de una sesión existente (sliding expiration) -- el índice
        // por sujeto debe seguir apuntando a ese mismo sessionId después, sin duplicarlo.
        var (cache, protector) = CreateInfrastructure();
        var store = new DistributedCacheBffSessionStore(cache, protector);
        var sessionId = await store.CreateAsync(SampleSession, TimeSpan.FromMinutes(30));
        await store.RenewAsync(sessionId, SampleSession, TimeSpan.FromMinutes(30));

        await store.RevokeAllForSubjectAsync("user-1");

        (await store.GetAsync(sessionId)).Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_WithADifferentDataProtectionKeyRing_ReturnsNull()
    {
        // Dos instancias con proveedores de Data Protection distintos (sin persistencia de claves
        // compartida) representan, por ejemplo, dos despliegues que no comparten el keyring -- el
        // valor cacheado de uno no debe poder leerse desde el otro.
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IDistributedCache>();

        var protectorA = DataProtectionProvider.Create("A");
        var protectorB = DataProtectionProvider.Create("B");

        var storeA = new DistributedCacheBffSessionStore(cache, protectorA);
        var storeB = new DistributedCacheBffSessionStore(cache, protectorB);

        var sessionId = await storeA.CreateAsync(SampleSession, TimeSpan.FromMinutes(30));
        var retrievedByB = await storeB.GetAsync(sessionId);

        retrievedByB.Should().BeNull();
    }
}
