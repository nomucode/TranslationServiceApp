using TranslationService.Application.Abstractions.Messaging;
using TranslationService.Application.Translations.Mapping;
using TranslationService.Contracts.Translations;
using TranslationService.Domain.Common;
using TranslationService.Domain.Repositories;
using TranslationService.Domain.Translations.ValueObjects;

namespace TranslationService.Application.Translations.Queries.GetTranslationJobById;

/// Lado de lectura: sin efectos secundarios y sin dependencias de escritura. Es lo que
/// permitiría, en un sistema real, apuntarlo a una réplica de sólo lectura sin tocar nada.
public sealed class GetTranslationJobByIdQueryHandler(ITranslationJobRepository repository)
    : IQueryHandler<GetTranslationJobByIdQuery, TranslationJobResponse>
{
    public async Task<Result<TranslationJobResponse>> HandleAsync(
        GetTranslationJobByIdQuery query,
        CancellationToken cancellationToken)
    {
        var jobId = JobId.Create(query.JobId);
        if (jobId.IsFailure)
        {
            return Result.Failure<TranslationJobResponse>(jobId.Error);
        }

        var found = await repository.GetByIdAsync(jobId.Value, cancellationToken);

        return found.IsFailure
            ? Result.Failure<TranslationJobResponse>(found.Error)
            : Result.Success(found.Value.ToResponse());
    }
}
