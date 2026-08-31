using System.ComponentModel.DataAnnotations;

namespace TranslationService.Infrastructure.Messaging;

public sealed class MessageQueueOptions
{
    public const string SectionName = "MessageQueue";

    /// Cola acotada: si el productor va más rápido que el worker, encolar espera en vez de
    /// consumir memoria sin límite. Es la contrapresión que daría un broker real.
    [Range(1, 100_000)]
    public int Capacity { get; init; } = 1_000;

    /// Cuántos trabajos se traducen a la vez. Traducir es E/S pura, así que el límite
    /// realista lo pone la cuota de Azure, no la CPU.
    [Range(1, 64)]
    public int MaxDegreeOfParallelism { get; init; } = 4;
}
