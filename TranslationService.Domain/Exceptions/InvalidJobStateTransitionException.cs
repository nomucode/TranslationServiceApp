using TranslationService.Domain.Translations;

namespace TranslationService.Domain.Exceptions;

/// Se lanza —en vez de devolver Result— porque una transición ilegal no es un caso de uso
/// esperado sino un defecto: alguien ha llamado al agregado fuera de orden.
public sealed class InvalidJobStateTransitionException(Guid jobId, TranslationStatus from, TranslationStatus to)
    : DomainException($"El trabajo {jobId} no puede transicionar de '{from}' a '{to}'.")
{
    public override string Code => "TranslationJob.InvalidTransition";

    public Guid JobId { get; } = jobId;

    public TranslationStatus From { get; } = from;

    public TranslationStatus To { get; } = to;
}
