using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Outbox;

/// <summary>
/// Host del relay de Outbox (F3-03): en loop, crea un scope de DI nuevo por ciclo (mismo motivo que
/// cualquier <see cref="BackgroundService"/> que consume servicios <c>Scoped</c> como el
/// <c>DbContext</c> — un <see cref="BackgroundService"/> vive como singleton durante todo el proceso,
/// nunca puede inyectar un servicio scoped directamente en su constructor), resuelve
/// <see cref="OutboxBatchProcessor"/> y le delega el trabajo real de un lote
/// (<see cref="OutboxBatchProcessor.ProcessBatchAsync"/>). La lógica de reclamar/publicar/marcar en sí
/// vive en <see cref="OutboxBatchProcessor"/>, no acá, para que un test pueda ejercitarla sin depender
/// del ciclo de vida de un <see cref="IHostedService"/> (incluida la prueba de dos instancias
/// concurrentes reclamando el mismo backlog).
/// </summary>
public sealed class OutboxPublisherBackgroundService(
    IServiceScopeFactory scopeFactory,
    OutboxPublisherOptions options,
    ILogger<OutboxPublisherBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var result = OutboxBatchResult.Empty;

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<OutboxBatchProcessor>();
                result = await processor.ProcessBatchAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Un fallo inesperado de todo el ciclo (por ejemplo, SQL Server momentáneamente no
                // disponible) no debe tumbar el worker: se loguea y se reintenta en el próximo
                // intervalo, igual que un fallo de publicación de una fila individual (ver
                // OutboxBatchProcessor).
                logger.LogError(ex, "Ciclo de OutboxPublisherBackgroundService falló; se reintentará en el próximo intervalo.");
            }

            if (result.Claimed > 0 && result.Claimed >= options.BatchSize)
            {
                // Lote lleno: probablemente queda más backlog pendiente. Sondear de nuevo de inmediato
                // en vez de esperar PollingInterval, para drenar más rápido.
                continue;
            }

            try
            {
                await Task.Delay(options.PollingInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
