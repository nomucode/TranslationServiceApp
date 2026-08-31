using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TranslationService.Application.Abstractions.Messaging;
using TranslationService.Application.Configuration;
using TranslationService.Domain.Common;
using TranslationService.Domain.Repositories;
using TranslationService.Domain.Translations;
using TranslationService.Domain.Translations.Events;
using TranslationService.Domain.Translations.ValueObjects;

namespace TranslationService.Application.Translations.Commands.CreateTranslationJob;

/// Lado de escritura del Asynchronous Request-Reply: acepta el trabajo, lo deja en Pending
/// y devuelve el control de inmediato. Ninguna llamada remota ocurre en el hilo de la petición.
public sealed class CreateTranslationJobCommandHandler(
    ITranslationJobRepository repository,
    IMessageQueue<TranslationRequestedEvent> queue,
    IOptions<TranslationOptions> options,
    TimeProvider timeProvider,
    ILogger<CreateTranslationJobCommandHandler> logger)
    : ICommandHandler<CreateTranslationJobCommand, JobId>
{
    public async Task<Result<JobId>> HandleAsync(
        CreateTranslationJobCommand command,
        CancellationToken cancellationToken)
    {
        var sourceText = SourceText.Create(command.Text);
        if (sourceText.IsFailure)
        {
            return Result.Failure<JobId>(sourceText.Error);
        }

        var targetLanguage = LanguageCode.Create(options.Value.TargetLanguage);
        if (targetLanguage.IsFailure)
        {
            logger.LogError(
                "El idioma destino configurado ('{TargetLanguage}') no es válido.",
                options.Value.TargetLanguage);

            return Result.Failure<JobId>(targetLanguage.Error);
        }

        var job = TranslationJob.Request(sourceText.Value, targetLanguage.Value, timeProvider.GetUtcNow());

        var persisted = await repository.AddAsync(job, cancellationToken);
        if (persisted.IsFailure)
        {
            return Result.Failure<JobId>(persisted.Error);
        }

        // El orden importa: encolar antes de persistir dejaría al worker despertando para un
        // job que todavía no existe. Persistir primero hace el flujo consistente incluso
        // si la escritura en la cola falla (el job quedaría Pending, recuperable).
        await queue.EnqueueAsync(new TranslationRequestedEvent(job.Id, job.RequestedAt), cancellationToken);

        logger.LogInformation("Trabajo de traducción {JobId} aceptado y encolado.", job.Id);

        return Result.Success(job.Id);
    }
}
