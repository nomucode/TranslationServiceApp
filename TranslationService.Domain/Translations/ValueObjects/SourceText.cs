using TranslationService.Domain.Common;
using TranslationService.Domain.Translations.Errors;

namespace TranslationService.Domain.Translations.ValueObjects;

public sealed record SourceText
{
    /// Límite del endpoint /translate de Azure para un único elemento del array.
    /// Validarlo aquí convierte un 400 remoto en un 400 local instantáneo y gratuito.
    public const int MaxLength = 5_000;

    private SourceText(string value) => Value = value;

    public string Value { get; }

    public static Result<SourceText> Create(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return Result.Failure<SourceText>(TranslationErrors.EmptySourceText);
        }

        var normalized = candidate.Trim();

        return normalized.Length > MaxLength
            ? Result.Failure<SourceText>(TranslationErrors.SourceTextTooLong)
            : Result.Success(new SourceText(normalized));
    }

    public override string ToString() => Value;
}
