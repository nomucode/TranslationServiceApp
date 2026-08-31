using TranslationService.Contracts.Translations;
using TranslationService.Domain.Translations;

namespace TranslationService.Application.Translations.Mapping;

/// Proyección del agregado al DTO de transporte. Aplana los Result<T> del dominio a los
/// valores por defecto del contrato, que es donde "ausencia" se representa como cadena vacía.
internal static class TranslationJobMapper
{
    public static TranslationJobResponse ToResponse(this TranslationJob job)
    {
        var outcome = job.Outcome;
        var completedAt = job.CompletedAt;

        return new TranslationJobResponse(
            JobId: job.Id.Value,
            Status: job.Status.ToContract(),
            SourceText: job.SourceText.Value,
            TargetLanguage: job.TargetLanguage.Value,
            TranslatedText: outcome.IsSuccess ? outcome.Value.Text.Value : string.Empty,
            DetectedLanguage: outcome.IsSuccess ? outcome.Value.DetectedLanguage.Value : string.Empty,
            WasTranslated: outcome.IsSuccess && outcome.Value.WasTranslated,
            FailureReason: job.FailureReason.Match(reason => reason, _ => string.Empty),
            RequestedAt: job.RequestedAt,
            ProcessingTimeMs: completedAt.IsSuccess
                ? (long)(completedAt.Value - job.RequestedAt).TotalMilliseconds
                : 0);
    }

    private static TranslationJobStatus ToContract(this TranslationStatus status) => status switch
    {
        TranslationStatus.Pending => TranslationJobStatus.Pending,
        TranslationStatus.Processing => TranslationJobStatus.Processing,
        TranslationStatus.Completed => TranslationJobStatus.Completed,
        TranslationStatus.Failed => TranslationJobStatus.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Estado de traducción no contemplado.")
    };
}
