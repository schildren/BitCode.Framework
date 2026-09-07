using System.Collections.Concurrent;
using BitCode.Framework.Shared.Application.Inbox;

namespace BitCode.Framework.Shared.Infrastructure.Messaging.Kafka.Tests;

/// <summary>
/// Test double de <see cref="IInboxMessageProcessor"/> puramente en memoria (<see cref="ConcurrentDictionary{TKey,TValue}"/>),
/// sin ningún <c>DbContext</c> real detrás: este proyecto (Shared.Infrastructure.Messaging.Kafka.Tests)
/// verifica el adapter Kafka en aislamiento (F3-02) — el mecanismo real de Inbox contra SQL Server
/// (F1-24) ya tiene su propia suite (<c>Shared.Infrastructure.Persistence.Tests</c>), y la coordinación
/// real Kafka + Inbox + SQL Server de punta a punta (F3-04) vive en
/// <c>InboxConsumerIntegrationTests</c> (mismo proyecto de Persistence, que combina
/// <c>SqlServerContainerFixture</c> y <c>KafkaContainerFixture</c>). Este double solo necesita
/// reproducir la SEMÁNTICA de deduplicación (mismo <c>messageId</c> descartado sin ejecutar
/// <c>handler</c> una segunda vez) para que <see cref="KafkaEventConsumer{TEvent}"/> pueda ejercitarse
/// aquí sin agregar una dependencia a SQL Server a este proyecto.
/// </summary>
public sealed class InMemoryInboxMessageProcessor : IInboxMessageProcessor
{
    private readonly ConcurrentDictionary<string, bool> _processedMessageIds = new();

    public async Task<InboxProcessOutcome> ProcessAsync(
        string messageId,
        string messageType,
        string payload,
        Func<CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
    {
        if (_processedMessageIds.ContainsKey(messageId))
        {
            return InboxProcessOutcome.Discarded;
        }

        await handler(cancellationToken).ConfigureAwait(false);
        _processedMessageIds[messageId] = true;

        return InboxProcessOutcome.Processed;
    }
}
