using TranslationService.Domain.Translations.ValueObjects;

namespace TranslationService.Domain.Translations;

/// WasTranslated distingue los dos caminos de éxito: una traducción real frente al
/// atajo de la regla de negocio (el texto ya venía en el idioma destino).
public sealed record TranslationOutcome(
    TranslatedText Text,
    LanguageCode DetectedLanguage,
    bool WasTranslated);
