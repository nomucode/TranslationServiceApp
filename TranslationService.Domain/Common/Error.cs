namespace TranslationService.Domain.Common;

/// El tipo permite que la capa Api traduzca un fallo de dominio al código HTTP correcto
/// sin conocer cada Error concreto (evita un switch gigante en los endpoints).
public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Failure
}

public sealed record Error(string Code, string Description, ErrorType Type)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    public static Error Validation(string code, string description) => new(code, description, ErrorType.Validation);

    public static Error NotFound(string code, string description) => new(code, description, ErrorType.NotFound);

    public static Error Conflict(string code, string description) => new(code, description, ErrorType.Conflict);

    public static Error Failure(string code, string description) => new(code, description, ErrorType.Failure);
}
