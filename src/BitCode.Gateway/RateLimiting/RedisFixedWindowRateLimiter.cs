using System.Threading.RateLimiting;
using StackExchange.Redis;

namespace BitCode.Gateway.RateLimiting;

/// <summary>
/// Rate limiter de ventana fija (fixed window) respaldado por Redis -- cierre del pendiente de F4-08
/// "rate limiting en memoria por réplica". Mismo algoritmo conceptual que
/// <c>RateLimiterOptionsExtensions.AddFixedWindowLimiter</c> nativo de ASP.NET Core, pero el contador
/// vive en Redis (compartido entre TODAS las réplicas del Gateway) en vez de en memoria de un único
/// proceso: con N réplicas, el limiter nativo en memoria deja pasar efectivamente N * PermitLimit
/// requests (cada réplica cuenta solo lo que ella misma recibe) -- este limiter usa un único contador
/// compartido, así que la suma de requests entre TODAS las réplicas respeta <see cref="_permitLimit"/>.
/// </summary>
/// <remarks>
/// Concurrencia: el incremento del contador y la fijación de su expiración ocurren dentro de un único
/// <c>EVAL</c> de Lua (<see cref="LuaScript"/>) -- Redis ejecuta cada script de principio a fin sin
/// interleaving con otro comando/script (garantía del propio motor, de un solo hilo para la ejecución de
/// comandos), así que dos réplicas del Gateway evaluando el script al mismo tiempo para la misma clave
/// nunca pisan el incremento de la otra ni compiten por fijar el TTL -- evita la condición de carrera de
/// un patrón ingenuo "GET, comparar en el cliente, SET" (leer-luego-escribir no atómico), que sí podría
/// dejar pasar más requests que <see cref="_permitLimit"/> bajo concurrencia real.
/// </remarks>
internal sealed class RedisFixedWindowRateLimiter : RateLimiter
{
    // INCRBY (no INCR) para soportar permitCount > 1 si algún día se pide costo variable por request.
    // PTTL < 0 identifica de forma atómica, dentro del mismo script, a la evaluación que ACABA de crear
    // la clave (una clave recién creada por INCRBY no tiene expiración todavía) -- more robusto que
    // comparar el valor devuelto contra permitCount, que se rompería si permitCount cambiara entre
    // llamadas.
    private const string LuaScript = """
        local current = redis.call('INCRBY', KEYS[1], ARGV[1])
        local ttl = redis.call('PTTL', KEYS[1])
        if ttl < 0 then
            redis.call('PEXPIRE', KEYS[1], ARGV[2])
        end
        if current > tonumber(ARGV[3]) then
            return 0
        end
        return 1
        """;

    private static readonly RateLimitLease SuccessLease = new RedisFixedWindowRateLimitLease(isAcquired: true);
    private static readonly RateLimitLease FailureLease = new RedisFixedWindowRateLimitLease(isAcquired: false);

    private readonly Func<IConnectionMultiplexer> _connectionFactory;
    private readonly string _redisKeyPrefix;
    private readonly int _permitLimit;
    private readonly TimeSpan _window;

    public RedisFixedWindowRateLimiter(
        Func<IConnectionMultiplexer> connectionFactory,
        string redisKeyPrefix,
        int permitLimit,
        TimeSpan window)
    {
        _connectionFactory = connectionFactory;
        _redisKeyPrefix = redisKeyPrefix;
        _permitLimit = permitLimit;
        _window = window;
    }

    /// <summary>
    /// No soportado -- no hay una noción de "tiempo ocioso" barata de calcular sin un round-trip a
    /// Redis por instancia, y ningún consumidor del framework la usa hoy (ASP.NET Core solo la expone
    /// como diagnóstico opcional). Mismo criterio que el resto de límites custom del ecosistema que no
    /// la implementan.
    /// </summary>
    public override TimeSpan? IdleDuration => null;

    /// <summary>
    /// No soportado por la misma razón que <see cref="IdleDuration"/> -- estadísticas agregadas
    /// (<c>CurrentAvailablePermits</c>, etc.) no tienen una lectura barata y atómica sin acoplar este
    /// limiter a un round-trip adicional en cada request.
    /// </summary>
    public override RateLimiterStatistics? GetStatistics() => null;

    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        var database = _connectionFactory().GetDatabase();
        var result = (long)database.ScriptEvaluate(LuaScript, [BuildRedisKey()], BuildArgs(permitCount));

        return result == 1 ? SuccessLease : FailureLease;
    }

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        var database = _connectionFactory().GetDatabase();
        var evalResult = await database
            .ScriptEvaluateAsync(LuaScript, [BuildRedisKey()], BuildArgs(permitCount))
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        return (long)evalResult == 1 ? SuccessLease : FailureLease;
    }

    private RedisValue[] BuildArgs(int permitCount) =>
    [
        permitCount,
        (long)_window.TotalMilliseconds,
        _permitLimit,
    ];

    /// <summary>
    /// Clave determinística por "bucket" de tiempo: todas las réplicas calculan la MISMA clave para la
    /// MISMA ventana (<c>floor(ahora / ventana)</c>) sin coordinación adicional entre ellas -- requiere
    /// relojes razonablemente sincronizados entre réplicas (NTP estándar de cualquier cluster; misma
    /// asunción implícita que cualquier TTL de Redis compartido entre procesos ya requiere). La ventana
    /// vieja expira sola vía <c>PEXPIRE</c> (ver <see cref="LuaScript"/>) -- nunca queda basura en Redis
    /// más allá de <see cref="_window"/> por clave.
    /// </summary>
    private RedisKey BuildRedisKey()
    {
        var windowSeconds = Math.Max(1, (long)_window.TotalSeconds);
        var bucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / windowSeconds;

        return $"{_redisKeyPrefix}:{bucket}";
    }

    private sealed class RedisFixedWindowRateLimitLease(bool isAcquired) : RateLimitLease
    {
        public override bool IsAcquired { get; } = isAcquired;

        public override IEnumerable<string> MetadataNames => [];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;
            return false;
        }
    }
}
