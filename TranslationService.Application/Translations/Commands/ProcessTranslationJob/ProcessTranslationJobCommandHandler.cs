using Microsoft.Extensions.Logging;
using TranslationService.Application.Abstractions.Messaging;
using TranslationService.Application.Abstractions.Translation;
using TranslationService.Domain.Common;
using TranslationService.Domain.Repositories;
using TranslationService.Domain.Translations;
using TranslationService.Domain.Translations.ValueObjects;

namespace TranslationService.Application.Translations.Commands.ProcessTranslationJob;

/// Consumidor del flujo asíncrono.
///
/// Contrato de retorno, deliberado: devuelve Success siempre que el job alcance un estado
/// terminal —incluido Failed—, porque el caso de uso "procesa este trabajo" se completó y
/// el desenlace quedó registrado. Sólo devuelve Failure cuando no se pudo registrar nada
/// (el job no existe, el repositorio no responde), que es lo único que el worker podría
/// querer reintentar.
public sealed class ProcessTranslationJobCommandHandler(
    ITranslationJobRepository repository,
    ITranslationProvider provider,
    TimeProvider timeProvider,
    ILogger<ProcessTranslationJobCommandHandler> logger)
    : ICommandHandler<ProcessTranslationJobCommand>
{
    public async Task<Result> HandleAsync(ProcessTranslationJobCommand command, CancellationToken cancellationToken)
    {
        var found = await repository.GetByIdAsync(command.JobId, cancellationToken);
        if (found.IsFailure)
        {
            logger.LogWarning("Se recibió un evento para el trabajo {JobId}, que no existe.", command.JobId);
            return Result.Failure(found.Error);
        }

        var job = found.Value;

        // Idempotencia: una cola puede reentregar. Si el trabajo ya salió de Pending,
        // alguien lo está atendiendo o ya terminó; reprocesarlo sobrescribiría el desenlace.
        if (job.Status is not TranslationStatus.Pending)
        {
            logger.LogInformation(
                "El trabajo {JobId} ya está en estado {Status}; se descarta la reentrega.",
                job.Id,
                job.Status);

            return Result.Success();
        }

        job.MarkAsProcessing();
        var marked = await repository.UpdateAsync(job, cancellationToken);
        if (marked.IsFailure)
        {
            return Result.Failure(marked.Error);
        }

        var translation = await TranslateSafelyAsync(job, cancellationToken);

        if (translation.IsFailure)
        {
            logger.LogWarning(
                "El trabajo {JobId} falló: {Reason}",
                job.Id,
                translation.Error.Description);

            job.Fail(translation.Error.Description, timeProvider.GetUtcNow());
        }
        else if (translation.Value.DetectedLanguage == job.TargetLanguage)
        {
            // Regla de negocio: Azure detectó el idioma destino. Se descarta su traducción
            // y el texto original se devuelve intacto.
            logger.LogInformation(
                "El trabajo {JobId} ya venía en '{Language}'; no se traduce.",
                job.Id,
                job.TargetLanguage);

            job.CompleteWithoutTranslation(translation.Value.DetectedLanguage, timeProvider.GetUtcNow());
        }
        else
        {
            job.CompleteAsTranslated(
                translation.Value.Text,
                translation.Value.DetectedLanguage,
                timeProvider.GetUtcNow());
        }

        return await repository.UpdateAsync(job, cancellationToken);
    }

    /// Red de seguridad: una excepción no contemplada del adaptador dejaría el trabajo
    /// atrapado en Processing para siempre, y el cliente sondeando de por vida.
    private async Task<Result<ProviderTranslation>> TranslateSafelyAsync(
        TranslationJob job,
        CancellationToken cancellationToken)
    {
        try
        {
            return await provider.TranslateAsync(job.SourceText, job.TargetLanguage, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Error inesperado traduciendo el trabajo {JobId}.", job.Id);

            return Result.Failure<ProviderTranslation>(
                Domain.Translations.Errors.TranslationErrors.ProviderUnavailable(exception.Message));
        }
    }
}
