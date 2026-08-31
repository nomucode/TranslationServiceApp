using TranslationService.Application.Abstractions.Messaging;
using TranslationService.Contracts.Translations;

namespace TranslationService.Application.Translations.Queries.GetTranslationJobById;

/// Recibe un Guid crudo y no un JobId: la query es la frontera con el mundo exterior y
/// es su handler quien tiene la responsabilidad de validarlo.
public sealed record GetTranslationJobByIdQuery(Guid JobId) : IQuery<TranslationJobResponse>;
