using BitCode.Framework.Shared.Application.Eventing;
using FluentAssertions;

namespace BitCode.Framework.Shared.Application.Tests.Eventing;

/// <summary>
/// F3-07 (Retries): cálculo puro de backoff exponencial con jitter, sin ninguna dependencia de
/// broker/base de datos real — <c>OutboxPublisherRetryTests</c> (Shared.Infrastructure.Persistence.Tests)
/// cubre el efecto end-to-end contra SQL Server real.
/// </summary>
public class EventRetryBackoffTests
{
    [Fact]
    public void CalculateDelay_BaseDelayZero_ReturnsZeroRegardlessOfAttempt()
    {
        var options = new EventRetryPolicyOptions { BaseDelay = TimeSpan.Zero };

        EventRetryBackoff.CalculateDelay(1, options).Should().Be(TimeSpan.Zero);
        EventRetryBackoff.CalculateDelay(5, options).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void CalculateDelay_GrowsExponentially_UpperBoundIncreasesPerAttempt()
    {
        // Random determinista que siempre devuelve el máximo del rango (1.0): aísla el efecto del
        // jitter y deja ver únicamente cómo crece el TOPE exponencial entre intentos sucesivos.
        var alwaysMaxRandom = new StubRandom(1.0);
        var options = new EventRetryPolicyOptions
        {
            BaseDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromMinutes(10), // suficientemente alto para no topar en los primeros intentos
        };

        var delayAttempt1 = EventRetryBackoff.CalculateDelay(1, options, alwaysMaxRandom);
        var delayAttempt2 = EventRetryBackoff.CalculateDelay(2, options, alwaysMaxRandom);
        var delayAttempt3 = EventRetryBackoff.CalculateDelay(3, options, alwaysMaxRandom);

        delayAttempt1.Should().Be(TimeSpan.FromSeconds(1), "intento 1: base * 2^0 = base");
        delayAttempt2.Should().Be(TimeSpan.FromSeconds(2), "intento 2: base * 2^1");
        delayAttempt3.Should().Be(TimeSpan.FromSeconds(4), "intento 3: base * 2^2");
        delayAttempt3.Should().BeGreaterThan(delayAttempt2);
        delayAttempt2.Should().BeGreaterThan(delayAttempt1);
    }

    [Fact]
    public void CalculateDelay_NeverExceedsMaxDelay_EvenAtHighAttemptNumbers()
    {
        var alwaysMaxRandom = new StubRandom(1.0);
        var options = new EventRetryPolicyOptions
        {
            BaseDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(30),
        };

        var delay = EventRetryBackoff.CalculateDelay(20, options, alwaysMaxRandom);

        delay.Should().Be(TimeSpan.FromSeconds(30), "el backoff exponencial sin tope excedería ampliamente MaxDelay en el intento 20");
    }

    [Fact]
    public void CalculateDelay_Jitter_StaysWithinZeroToExponentialUpperBound()
    {
        var options = new EventRetryPolicyOptions
        {
            BaseDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromMinutes(10),
        };
        var upperBoundForAttempt3 = TimeSpan.FromSeconds(4); // base(1s) * 2^(3-1)

        for (var i = 0; i < 200; i++)
        {
            var delay = EventRetryBackoff.CalculateDelay(3, options, Random.Shared);
            delay.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
            delay.Should().BeLessThanOrEqualTo(upperBoundForAttempt3);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void CalculateDelay_NonPositiveAttemptNumber_TreatedAsFirstAttempt(int attemptNumber)
    {
        var options = new EventRetryPolicyOptions { BaseDelay = TimeSpan.FromSeconds(1), MaxDelay = TimeSpan.FromMinutes(10) };
        var alwaysMaxRandom = new StubRandom(1.0);

        var delay = EventRetryBackoff.CalculateDelay(attemptNumber, options, alwaysMaxRandom);

        delay.Should().Be(TimeSpan.FromSeconds(1), "intentos <= 0 se tratan como el primer intento (exponente 0)");
    }

    [Theory]
    [InlineData(9, 10, false)]
    [InlineData(10, 10, true)]
    [InlineData(11, 10, true)]
    public void IsExhausted_ComparesAttemptsAgainstMaxAttempts(int attemptsSoFar, int maxAttempts, bool expected)
    {
        var options = new EventRetryPolicyOptions { MaxAttempts = maxAttempts };

        EventRetryBackoff.IsExhausted(attemptsSoFar, options).Should().Be(expected);
    }

    /// <summary>
    /// <see cref="Random"/> determinista: <see cref="Random.NextDouble"/> siempre devuelve el mismo
    /// valor fijo, para aislar el cálculo del tope exponencial del ruido del jitter real en las pruebas
    /// que necesitan un resultado exacto y reproducible.
    /// </summary>
    private sealed class StubRandom(double fixedValue) : Random
    {
        public override double NextDouble() => fixedValue;
    }
}
