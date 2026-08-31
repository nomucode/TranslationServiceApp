using TranslationService.Domain.Common;
using TranslationService.Domain.Translations.ValueObjects;

namespace TranslationService.Application.Abstractions.Translation;

/// Puerto hacia el servicio de traducción externo. Devuelve Result porque un proveedor
/// remoto caído es un desenlace esperado del flujo, no un defecto del programa.
public interface ITranslationProvider
{
    Task<Result<ProviderTranslation>> TranslateAsync(
        SourceText sourceText,
        LanguageCode targetLanguage,
        CancellationToken cancellationToken);
}

/// Azure resuelve detección y traducción en una única llamada a /translate, así que el
/// puerto devuelve ambas cosas juntas y es el dominio quien decide qué hacer con ellas.
public sealed record ProviderTranslation(TranslatedText Text, LanguageCode DetectedLanguage);
