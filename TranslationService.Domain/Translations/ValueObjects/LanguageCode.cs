using System.Text.RegularExpressions;
using TranslationService.Domain.Common;
using TranslationService.Domain.Translations.Errors;

namespace TranslationService.Domain.Translations.ValueObjects;

public sealed partial record LanguageCode
{
    public static readonly LanguageCode Spanish = new("es");

    private LanguageCode(string value) => Value = value;

    public string Value { get; }

    public static Result<LanguageCode> Create(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return Result.Failure<LanguageCode>(TranslationErrors.InvalidLanguageCode);
        }

        // Se normaliza a minúsculas para que la comparación "detectado == destino" —el eje
        // de la regla de negocio— no dependa de cómo case Azure la respuesta ('ES' vs 'es').
        var normalized = candidate.Trim().ToLowerInvariant();

        return Bcp47().IsMatch(normalized)
            ? Result.Success(new LanguageCode(normalized))
            : Result.Failure<LanguageCode>(TranslationErrors.InvalidLanguageCode);
    }

    public override string ToString() => Value;

    /// Subconjunto pragmático de BCP-47: idioma de 2-3 letras y subetiquetas opcionales.
    [GeneratedRegex(@"^[a-z]{2,3}(-[a-z0-9]{2,8})*$", RegexOptions.CultureInvariant)]
    private static partial Regex Bcp47();
}
