using TranslationService.Domain.Common;
using TranslationService.Domain.Translations;
using TranslationService.Domain.Translations.ValueObjects;

namespace TranslationService.Domain.Repositories;

/// Puerto de salida. Vive en Domain por convención DDD: es la colección de agregados,
/// no un detalle técnico. La implementación (en memoria hoy, SQL mañana) vive en Infrastructure.
public interface ITranslationJobRepository
{
    Task<Result> AddAsync(TranslationJob job, CancellationToken cancellationToken = default);

    Task<Result<TranslationJob>> GetByIdAsync(JobId id, CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(TranslationJob job, CancellationToken cancellationToken = default);
}
