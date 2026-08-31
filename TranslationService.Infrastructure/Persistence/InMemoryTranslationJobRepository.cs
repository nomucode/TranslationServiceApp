using System.Collections.Concurrent;
using TranslationService.Domain.Common;
using TranslationService.Domain.Repositories;
using TranslationService.Domain.Translations;
using TranslationService.Domain.Translations.Errors;
using TranslationService.Domain.Translations.ValueObjects;

namespace TranslationService.Infrastructure.Persistence;

/// Sustituto de la base de datos para el alcance de la prueba. Es singleton y usa
/// ConcurrentDictionary porque hay dos escritores concurrentes reales: el hilo de la
/// petición HTTP que crea el trabajo y el worker que lo procesa.
///
/// Limitación asumida y consciente: el diccionario guarda la *referencia viva* del
/// agregado, no una copia, así que las mutaciones del worker ya son visibles antes de
/// llamar a UpdateAsync. Se mantiene la llamada igualmente para que el puerto tenga la
/// misma forma que tendría contra EF Core o Dapper, y sustituir esta clase no obligue a
/// tocar la capa Application.
public sealed class InMemoryTranslationJobRepository : ITranslationJobRepository
{
    private readonly ConcurrentDictionary<JobId, TranslationJob> _jobs = new();

    public Task<Result> AddAsync(TranslationJob job, CancellationToken cancellationToken = default) =>
        Task.FromResult(_jobs.TryAdd(job.Id, job)
            ? Result.Success()
            : Result.Failure(TranslationErrors.DuplicateJob));

    public Task<Result<TranslationJob>> GetByIdAsync(JobId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_jobs.TryGetValue(id, out var job)
            ? Result.Success(job)
            : Result.Failure<TranslationJob>(TranslationErrors.JobNotFound));

    public Task<Result> UpdateAsync(TranslationJob job, CancellationToken cancellationToken = default)
    {
        if (!_jobs.ContainsKey(job.Id))
        {
            return Task.FromResult(Result.Failure(TranslationErrors.JobNotFound));
        }

        _jobs[job.Id] = job;

        return Task.FromResult(Result.Success());
    }
}
