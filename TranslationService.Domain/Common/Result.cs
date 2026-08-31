namespace TranslationService.Domain.Common;

/// Resultado explícito para los fallos *esperados* del flujo (entrada inválida, job
/// inexistente, proveedor caído). Los fallos *inesperados* —violar la máquina de estados
/// del agregado— siguen siendo excepciones: son bugs, no casos de uso.
public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
        {
            throw new InvalidOperationException("Un resultado correcto no puede transportar un error.");
        }

        if (!isSuccess && error == Error.None)
        {
            throw new InvalidOperationException("Un resultado fallido debe transportar un error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);

    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);
}

public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error) => _value = value;

    /// Acceder al valor de un resultado fallido es un error de programación, no un caso
    /// de uso: por eso lanza en vez de devolver null.
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"No se puede leer el valor de un resultado fallido ({Error.Code}).");

    public static implicit operator Result<TValue>(TValue value) => Success(value);

    public TResult Match<TResult>(Func<TValue, TResult> onSuccess, Func<Error, TResult> onFailure) =>
        IsSuccess ? onSuccess(Value) : onFailure(Error);
}
