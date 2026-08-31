using System.Collections.Concurrent;
using TranslationService.Application.Abstractions.Translation;
using TranslationService.Domain.Common;
using TranslationService.Domain.Translations.Errors;
using TranslationService.Domain.Translations.ValueObjects;

namespace TranslationService.Tests.Api;

/// Sustituto de Azure para los tests de integración: guioniza la respuesta por texto de
/// entrada. Todo lo demás del host —endpoints, cola, worker, repositorio— es el real.
internal sealed class StubTranslationProvider : ITranslationProvider
{
    private readonly ConcurrentDictionary<string, Result<ProviderTranslation>> _responses = new();

    public TimeSpan Latency { get; set; } = TimeSpan.Zero;

    public void Returns(string sourceText, string translated, string detectedLanguage) =>
        _responses[sourceText] = Result.Success(new ProviderTranslation(
            TranslatedText.Create(translated).Value,
            LanguageCode.Create(detectedLanguage).Value));

    public void Fails(string sourceText, string reason) =>
        _responses[sourceText] = Result.Failure<ProviderTranslation>(
            TranslationErrors.ProviderUnavailable(reason));

    public async Task<Result<ProviderTranslation>> TranslateAsync(
        SourceText sourceText,
        LanguageCode targetLanguage,
        CancellationToken cancellationToken)
    {
        if (Latency > TimeSpan.Zero)
        {
            await Task.Delay(Latency, cancellationToken);
        }

        return _responses.TryGetValue(sourceText.Value, out var response)
            ? response
            : Result.Success(new ProviderTranslation(
                TranslatedText.Create($"[traducido] {sourceText.Value}").Value,
                LanguageCode.Create("en").Value));
    }
}
