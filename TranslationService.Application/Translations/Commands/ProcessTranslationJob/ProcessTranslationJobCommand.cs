using TranslationService.Application.Abstractions.Messaging;
using TranslationService.Domain.Translations.ValueObjects;

namespace TranslationService.Application.Translations.Commands.ProcessTranslationJob;

public sealed record ProcessTranslationJobCommand(JobId JobId) : ICommand;
