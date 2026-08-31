namespace TranslationService.Contracts.Translations;

/// DTO deliberadamente plano y sin nullables: Status es el discriminante que indica qué
/// campos son significativos. Se apoya en una garantía del dominio —TranslatedText nunca
/// puede ser vacío— de modo que la cadena vacía significa inequívocamente "aún no disponible".
/// Evita que el cliente tenga que distinguir entre null, ausente y vacío.
public sealed record TranslationJobResponse(
    Guid JobId,
    TranslationJobStatus Status,
    string SourceText,
    string TargetLanguage,
    string TranslatedText,
    string DetectedLanguage,
    bool WasTranslated,
    string FailureReason,
    DateTimeOffset RequestedAt,
    long ProcessingTimeMs)
{
    public bool IsTerminal => Status is TranslationJobStatus.Completed or TranslationJobStatus.Failed;
}
