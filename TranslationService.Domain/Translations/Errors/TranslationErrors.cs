using TranslationService.Domain.Common;

namespace TranslationService.Domain.Translations.Errors;

/// Catálogo único de errores del dominio de traducción. Centralizarlos evita cadenas
/// mágicas repartidas por las capas y da a la Api un contrato estable de códigos.
public static class TranslationErrors
{
    public static readonly Error EmptyJobId =
        Error.Validation("JobId.Empty", "El identificador del trabajo no puede estar vacío.");

    public static readonly Error MalformedJobId =
        Error.Validation("JobId.Malformed", "El identificador del trabajo no es un GUID válido.");

    public static readonly Error EmptySourceText =
        Error.Validation("SourceText.Empty", "El texto a traducir no puede estar vacío.");

    public static readonly Error SourceTextTooLong =
        Error.Validation("SourceText.TooLong", $"El texto a traducir no puede superar los {ValueObjects.SourceText.MaxLength} caracteres.");

    public static readonly Error EmptyTranslatedText =
        Error.Validation("TranslatedText.Empty", "El texto traducido no puede estar vacío.");

    public static readonly Error InvalidLanguageCode =
        Error.Validation("LanguageCode.Invalid", "El código de idioma no cumple el formato BCP-47 esperado (ej. 'es', 'pt-br').");

    public static readonly Error JobNotFound =
        Error.NotFound("TranslationJob.NotFound", "No existe ningún trabajo de traducción con ese identificador.");

    public static readonly Error NoOutcomeYet =
        Error.Conflict("TranslationJob.NoOutcome", "El trabajo de traducción todavía no ha producido un resultado.");

    public static readonly Error NotFailed =
        Error.Conflict("TranslationJob.NotFailed", "El trabajo de traducción no ha fallado.");

    public static readonly Error NotCompleted =
        Error.Conflict("TranslationJob.NotCompleted", "El trabajo de traducción todavía no ha finalizado.");

    public static Error ProviderUnavailable(string reason) =>
        Error.Failure("TranslationProvider.Unavailable", reason);
}
