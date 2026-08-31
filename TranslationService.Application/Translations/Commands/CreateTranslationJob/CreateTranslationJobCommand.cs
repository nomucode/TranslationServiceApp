using TranslationService.Application.Abstractions.Messaging;
using TranslationService.Domain.Translations.ValueObjects;

namespace TranslationService.Application.Translations.Commands.CreateTranslationJob;

public sealed record CreateTranslationJobCommand(string Text) : ICommand<JobId>;
