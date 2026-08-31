using TranslationService.Domain.Translations.ValueObjects;

namespace TranslationService.Domain.Translations.Events;

/// El evento transporta sólo el identificador, no el texto: el worker recarga el agregado
/// desde el repositorio y trabaja siempre sobre el estado vigente, no sobre una foto.
public sealed record TranslationRequestedEvent(JobId JobId, DateTimeOffset OccurredAt);
