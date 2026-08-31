using TranslationService.Domain.Common;
using TranslationService.Domain.Translations.Errors;

namespace TranslationService.Domain.Translations.ValueObjects;

/// Struct por rendimiento (se compara y se usa como clave de diccionario constantemente).
/// Limitación consciente de C#: `default(JobId)` sigue siendo construible; las factorías
/// son el único camino de entrada válido y el agregado nunca lo expone sin inicializar.
public readonly record struct JobId
{
    private JobId(Guid value) => Value = value;

    public Guid Value { get; }

    /// UUIDv7: ordenable por tiempo. Si mañana esto va a una base de datos real, los ids
    /// secuenciales evitan la fragmentación del índice clúster que provoca Guid.NewGuid().
    public static JobId New() => new(Guid.CreateVersion7());

    public static Result<JobId> Create(Guid value) => value == Guid.Empty
        ? Result.Failure<JobId>(TranslationErrors.EmptyJobId)
        : Result.Success(new JobId(value));

    public static Result<JobId> TryParse(string? candidate) =>
        Guid.TryParse(candidate, out var parsed)
            ? Create(parsed)
            : Result.Failure<JobId>(TranslationErrors.MalformedJobId);

    public override string ToString() => Value.ToString();
}
