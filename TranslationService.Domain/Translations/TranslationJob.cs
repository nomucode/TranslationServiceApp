using TranslationService.Domain.Common;
using TranslationService.Domain.Exceptions;
using TranslationService.Domain.Translations.Errors;
using TranslationService.Domain.Translations.ValueObjects;

namespace TranslationService.Domain.Translations;

/// Aggregate Root. Todo cambio de estado pasa por un método de intención (no hay setters
/// públicos) y cada uno valida la transición antes de aplicarla.
public sealed class TranslationJob
{
    private const string UnspecifiedFailure = "El trabajo de traducción falló por un motivo no especificado.";

    private TranslationOutcome? _outcome;
    private DateTimeOffset? _completedAt;
    private string _failureReason = string.Empty;

    private TranslationJob(JobId id, SourceText sourceText, LanguageCode targetLanguage, DateTimeOffset requestedAt)
    {
        Id = id;
        SourceText = sourceText;
        TargetLanguage = targetLanguage;
        RequestedAt = requestedAt;
        Status = TranslationStatus.Pending;
    }

    public JobId Id { get; }

    public SourceText SourceText { get; }

    public LanguageCode TargetLanguage { get; }

    public DateTimeOffset RequestedAt { get; }

    public TranslationStatus Status { get; private set; }

    /// Ningún miembro público devuelve null: la ausencia de resultado se modela como un
    /// Result fallido, de modo que el consumidor esté obligado a contemplar ese camino.
    public Result<TranslationOutcome> Outcome => _outcome is null
        ? Result.Failure<TranslationOutcome>(TranslationErrors.NoOutcomeYet)
        : Result.Success(_outcome);

    public Result<DateTimeOffset> CompletedAt => _completedAt is null
        ? Result.Failure<DateTimeOffset>(TranslationErrors.NotCompleted)
        : Result.Success(_completedAt.Value);

    public Result<string> FailureReason => Status is TranslationStatus.Failed
        ? Result.Success(_failureReason)
        : Result.Failure<string>(TranslationErrors.NotFailed);

    public static TranslationJob Request(SourceText sourceText, LanguageCode targetLanguage, DateTimeOffset requestedAt) =>
        new(JobId.New(), sourceText, targetLanguage, requestedAt);

    public void MarkAsProcessing()
    {
        EnsureTransitionAllowed(TranslationStatus.Processing, TranslationStatus.Pending);
        Status = TranslationStatus.Processing;
    }

    public void CompleteAsTranslated(TranslatedText translatedText, LanguageCode detectedLanguage, DateTimeOffset completedAt)
    {
        EnsureTransitionAllowed(TranslationStatus.Completed, TranslationStatus.Processing);

        if (detectedLanguage == TargetLanguage)
        {
            // Si el idioma detectado ya es el destino, el camino correcto es
            // CompleteWithoutTranslation. Aceptarlo aquí falsearía WasTranslated.
            throw new InvalidJobStateTransitionException(Id.Value, Status, TranslationStatus.Completed);
        }

        Settle(new TranslationOutcome(translatedText, detectedLanguage, WasTranslated: true), completedAt);
    }

    /// Regla de negocio: Azure detectó el idioma destino, así que no se traduce nada y el
    /// texto original se devuelve intacto.
    public void CompleteWithoutTranslation(LanguageCode detectedLanguage, DateTimeOffset completedAt)
    {
        EnsureTransitionAllowed(TranslationStatus.Completed, TranslationStatus.Processing);

        if (detectedLanguage != TargetLanguage)
        {
            throw new InvalidJobStateTransitionException(Id.Value, Status, TranslationStatus.Completed);
        }

        Settle(
            new TranslationOutcome(TranslatedText.From(SourceText), detectedLanguage, WasTranslated: false),
            completedAt);
    }

    public void Fail(string reason, DateTimeOffset failedAt)
    {
        EnsureTransitionAllowed(TranslationStatus.Failed, TranslationStatus.Pending, TranslationStatus.Processing);

        _failureReason = string.IsNullOrWhiteSpace(reason) ? UnspecifiedFailure : reason.Trim();
        _completedAt = failedAt;
        Status = TranslationStatus.Failed;
    }

    /// El orden importa: el repositorio en memoria comparte la referencia del agregado con
    /// los lectores, así que el estado terminal se publica *después* de su carga útil para
    /// que un GET concurrente nunca vea 'Completed' sin resultado.
    private void Settle(TranslationOutcome outcome, DateTimeOffset completedAt)
    {
        _outcome = outcome;
        _completedAt = completedAt;
        Status = TranslationStatus.Completed;
    }

    private void EnsureTransitionAllowed(TranslationStatus target, params ReadOnlySpan<TranslationStatus> allowedOrigins)
    {
        foreach (var origin in allowedOrigins)
        {
            if (Status == origin)
            {
                return;
            }
        }

        throw new InvalidJobStateTransitionException(Id.Value, Status, target);
    }
}
