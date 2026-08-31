namespace TranslationService.Client.Services;

/// Equivalente ligero del Result<T> del dominio para el lado del cliente.
///
/// No se reutiliza el del dominio a propósito: el WASM sólo referencia Contracts, y hacer
/// que el navegador cargase el ensamblado de Domain sólo por un tipo de utilidad rompería
/// la regla de dependencias por comodidad.
public readonly record struct ApiResult<TValue>
{
    private readonly TValue? _value;

    private ApiResult(TValue? value, bool isSuccess, string error)
    {
        _value = value;
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public string Error { get; }

    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("No se puede leer el valor de un resultado fallido.");

    public static ApiResult<TValue> Ok(TValue value) => new(value, true, string.Empty);

    public static ApiResult<TValue> Fail(string error) => new(default, false, error);
}
