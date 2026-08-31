using TranslationService.Domain.Common;
using TranslationService.Domain.Translations.Errors;

namespace TranslationService.Domain.Translations.ValueObjects;

public sealed record TranslatedText
{
    private TranslatedText(string value) => Value = value;

    public string Value { get; }

    public static Result<TranslatedText> Create(string? candidate) =>
        string.IsNullOrWhiteSpace(candidate)
            ? Result.Failure<TranslatedText>(TranslationErrors.EmptyTranslatedText)
            : Result.Success(new TranslatedText(candidate));

    /// Camino sin validación para la regla "el texto ya está en el idioma destino":
    /// un SourceText ya es, por construcción, una cadena no vacía.
    public static TranslatedText From(SourceText source) => new(source.Value);

    public override string ToString() => Value;
}
