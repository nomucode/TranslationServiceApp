using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TranslationService.Application.Abstractions.Messaging;
using TranslationService.Application.Translations.Commands.ProcessTranslationJob;
using TranslationService.Domain.Translations.Events;

namespace TranslationService.Infrastructure.Messaging;

/// El consumidor del Asynchronous Request-Reply. Es el único punto del sistema donde se
/// llama a Azure, siempre fuera del hilo de la petición HTTP.
public sealed class TranslationJobWorker(
    IMessageQueue<TranslationRequestedEvent> queue,
    IServiceScopeFactory scopeFactory,
    IOptions<MessageQueueOptions> options,
    ILogger<TranslationJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var maxDegreeOfParallelism = options.Value.MaxDegreeOfParallelism;

        logger.LogInformation(
            "Worker de traducción iniciado con un paralelismo máximo de {MaxDegreeOfParallelism}.",
            maxDegreeOfParallelism);

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism,
            CancellationToken = stoppingToken
        };

        try
        {
            // Parallel.ForEachAsync sobre el IAsyncEnumerable de la cola: consume tantos
            // mensajes a la vez como permita el grado de paralelismo, sin gestionar tareas
            // a mano ni bloquear hilos del pool esperando E/S.
            await Parallel.ForEachAsync(queue.DequeueAllAsync(stoppingToken), parallelOptions, ProcessAsync);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Worker de traducción detenido.");
        }
    }

    private async ValueTask ProcessAsync(TranslationRequestedEvent message, CancellationToken cancellationToken)
    {
        // Un scope por mensaje, igual que ASP.NET Core hace por petición: los handlers son
        // Scoped y no deben compartir estado entre trabajos.
        await using var scope = scopeFactory.CreateAsyncScope();

        try
        {
            var handler = scope.ServiceProvider
                .GetRequiredService<ICommandHandler<ProcessTranslationJobCommand>>();

            var result = await handler.HandleAsync(
                new ProcessTranslationJobCommand(message.JobId),
                cancellationToken);

            if (result.IsFailure)
            {
                logger.LogError(
                    "No se pudo procesar el trabajo {JobId}: {ErrorCode} - {ErrorDescription}",
                    message.JobId,
                    result.Error.Code,
                    result.Error.Description);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Imprescindible: desde .NET 6 una excepción que escape de un BackgroundService
            // tumba el host entero. Sin este catch, un único mensaje defectuoso dejaría la
            // cola sin consumir —y la aplicación caída— en silencio.
            logger.LogError(exception, "Error no controlado procesando el trabajo {JobId}.", message.JobId);
        }
    }
}
