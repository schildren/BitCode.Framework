using System.Text;
using System.Text.Json;
using BitCode.Framework.Shared.Application.Eventing;
using Confluent.Kafka;

namespace BitCode.Framework.Shared.Infrastructure.Messaging.Kafka;

/// <summary>
/// Serializa/deserializa un <see cref="IIntegrationEvent"/> hacia/desde el mensaje Kafka concreto
/// (F3-02: "serializers").
/// </summary>
/// <remarks>
/// Formato elegido: JSON (<c>System.Text.Json</c>) del tipo .NET concreto del evento (mismo criterio
/// que <c>OutboxSaveChangesInterceptor</c> usa para <c>DomainEvent</c>, F1-23) como <c>Value</c> del
/// mensaje — preserva toda la forma del evento sin necesitar un registro de esquema adicional
/// (Avro/Protobuf con Schema Registry queda fuera de alcance de F3-02; la compatibilidad
/// forward/backward del JSON es trabajo de F3-06). Además de ir en el payload, <see cref="EventType"/>,
/// <see cref="SchemaVersion"/>, <see cref="EventId"/> y <see cref="OccurredOnUtc"/> se repiten como
/// headers Kafka (nombres con prefijo <c>bitcode-</c>) para que observabilidad/enrutamiento (F3-10) o
/// un filtro de consumidor puedan leerlos sin deserializar el payload completo — el payload sigue
/// siendo la fuente de verdad; los headers son una copia de conveniencia, nunca al revés.
/// </remarks>
public static class KafkaIntegrationEventSerializer
{
    public const string EventTypeHeader = "bitcode-event-type";
    public const string SchemaVersionHeader = "bitcode-schema-version";
    public const string EventIdHeader = "bitcode-event-id";
    public const string OccurredOnUtcHeader = "bitcode-occurred-on-utc";

    /// <summary>Serializa <paramref name="integrationEvent"/> a los bytes de <c>Value</c> y a los headers del mensaje Kafka.</summary>
    public static (byte[] Value, Headers Headers) Serialize(IIntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var value = JsonSerializer.SerializeToUtf8Bytes(integrationEvent, integrationEvent.GetType());

        var headers = new Headers
        {
            { EventTypeHeader, Encoding.UTF8.GetBytes(integrationEvent.EventType) },
            { SchemaVersionHeader, Encoding.UTF8.GetBytes(integrationEvent.SchemaVersion.ToString()) },
            { EventIdHeader, Encoding.UTF8.GetBytes(integrationEvent.EventId.ToString()) },
            { OccurredOnUtcHeader, Encoding.UTF8.GetBytes(integrationEvent.OccurredOnUtc.ToString("O")) },
        };

        return (value, headers);
    }

    /// <summary>Deserializa el <c>Value</c> de un mensaje Kafka al tipo concreto <typeparamref name="TEvent"/>.</summary>
    public static TEvent Deserialize<TEvent>(byte[] value)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(value);

        return JsonSerializer.Deserialize<TEvent>(value)
            ?? throw new InvalidOperationException(
                $"El payload Kafka deserializó a null para el tipo {typeof(TEvent).FullName}.");
    }
}
