using TranslationService.Domain.Common;

namespace TranslationService.Api.Extensions;

/// Única traducción de Result a HTTP de todo el proyecto. Centralizarla evita repetir el
/// `if (result.IsFailure)` en cada endpoint y, sobre todo, garantiza que un mismo tipo de
/// error siempre produzca el mismo código de estado.
internal static class ResultExtensions
{
    public static IResult Match<TValue>(this Result<TValue> result, Func<TValue, IResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value) : result.Error.ToProblem();

    public static IResult ToProblem(this Error error) => Results.Problem(
        title: TitleFor(error.Type),
        detail: error.Description,
        statusCode: StatusCodeFor(error.Type),
        // El código de dominio viaja como extensión del ProblemDetails: el cliente puede
        // ramificar por 'SourceText.TooLong' sin tener que parsear el texto del mensaje.
        extensions: new Dictionary<string, object?> { ["code"] = error.Code });

    /// El ErrorType del dominio es lo que hace innecesario un switch por cada Error concreto.
    private static int StatusCodeFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError
    };

    private static string TitleFor(ErrorType type) => type switch
    {
        ErrorType.Validation => "La petición no es válida",
        ErrorType.NotFound => "Recurso no encontrado",
        ErrorType.Conflict => "Conflicto con el estado actual del recurso",
        _ => "Se ha producido un error inesperado"
    };
}
