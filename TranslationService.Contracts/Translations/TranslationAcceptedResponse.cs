namespace TranslationService.Contracts.Translations;

/// Cuerpo del 202 Accepted. StatusUrl materializa el Asynchronous Request-Reply: el
/// cliente no compone la URL de sondeo, la recibe.
public sealed record TranslationAcceptedResponse(
    Guid JobId,
    TranslationJobStatus Status,
    string StatusUrl);
