using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;
using TranslationService.Application.Abstractions.Translation;
using TranslationService.Domain.Common;
using TranslationService.Domain.Translations.Errors;
using TranslationService.Domain.Translations.ValueObjects;

namespace TranslationService.Infrastructure.Translation;

/// Adaptador de salida hacia Azure Translator v3.
///
/// Traduce todo fallo remoto —HTTP, timeout, circuito abierto o respuesta malformada— a un
/// Result. Que Application no vea nunca una excepción de red es lo que permite que el
/// agregado registre un estado Failed legible en lugar de que el worker se caiga.
internal sealed class AzureTranslatorClient(
    HttpClient httpClient,
    IOptions<AzureTranslatorOptions> options,
    ILogger<AzureTranslatorClient> logger) : ITranslationProvider
{
    public async Task<Result<ProviderTranslation>> TranslateAsync(
        SourceText sourceText,
        LanguageCode targetLanguage,
        CancellationToken cancellationToken)
    {
        // Se omite deliberadamente el parámetro 'from': Azure autodetecta el idioma y lo
        // devuelve en detectedLanguage, de modo que detección y traducción se resuelven en
        // una única llamada en lugar de encadenar /detect + /translate.
        var requestUri = $"translate?api-version={options.Value.ApiVersion}&to={Uri.EscapeDataString(targetLanguage.Value)}";

        AzureTranslateRequestItem[] payload = [new(sourceText.Value)];

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                requestUri,
                payload,
                AzureTranslatorJsonContext.Default.AzureTranslateRequestItemArray,
                cancellationToken);

            return response.IsSuccessStatusCode
                ? await ReadTranslationAsync(response, cancellationToken)
                : await ReadErrorAsync(response, cancellationToken);
        }
        catch (BrokenCircuitException)
        {
            // El circuito abierto es una protección, no una anomalía: se corta rápido para
            // no seguir castigando a un servicio que ya se sabe caído.
            logger.LogWarning("El circuito hacia Azure Translator está abierto; no se intenta la llamada.");

            return Result.Failure<ProviderTranslation>(TranslationErrors.ProviderUnavailable(
                "El servicio de traducción no está disponible temporalmente (circuito abierto)."));
        }
        catch (Exception exception) when (exception is TimeoutRejectedException or TaskCanceledException
                                          && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "La llamada a Azure Translator agotó el tiempo de espera.");

            return Result.Failure<ProviderTranslation>(TranslationErrors.ProviderUnavailable(
                "El servicio de traducción agotó el tiempo de espera."));
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Error de red llamando a Azure Translator.");

            return Result.Failure<ProviderTranslation>(TranslationErrors.ProviderUnavailable(
                $"No se pudo contactar con el servicio de traducción: {exception.Message}"));
        }
    }

    private static async Task<Result<ProviderTranslation>> ReadTranslationAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var results = await response.Content.ReadFromJsonAsync(
            AzureTranslatorJsonContext.Default.IReadOnlyListAzureTranslateResultItem,
            cancellationToken);

        var item = results?.FirstOrDefault();
        if (item is null)
        {
            return MalformedResponse("la respuesta no contenía ningún resultado");
        }

        // Sin idioma detectado no se puede aplicar la regla de negocio, así que se falla de
        // forma explícita en lugar de adivinar.
        var detectedLanguage = LanguageCode.Create(item.DetectedLanguage?.Language);
        if (detectedLanguage.IsFailure)
        {
            return MalformedResponse("la respuesta no incluía un idioma detectado válido");
        }

        var translatedText = TranslatedText.Create(item.Translations?.FirstOrDefault()?.Text);
        if (translatedText.IsFailure)
        {
            return MalformedResponse("la respuesta no incluía ningún texto traducido");
        }

        return Result.Success(new ProviderTranslation(translatedText.Value, detectedLanguage.Value));
    }

    private async Task<Result<ProviderTranslation>> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        // Azure describe sus fallos en un sobre {"error":{"code":..,"message":".."}}.
        // Propagarlo hace que el motivo del job fallido sea accionable en lugar de un 401 pelado.
        var detail = await TryReadAzureErrorAsync(response, cancellationToken);

        logger.LogWarning(
            "Azure Translator respondió {StatusCode}: {Detail}",
            (int)response.StatusCode,
            detail);

        return Result.Failure<ProviderTranslation>(TranslationErrors.ProviderUnavailable(
            $"El servicio de traducción respondió {(int)response.StatusCode} ({response.StatusCode}): {detail}"));
    }

    private static async Task<string> TryReadAzureErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var envelope = await response.Content.ReadFromJsonAsync(
                AzureTranslatorJsonContext.Default.AzureErrorEnvelope,
                cancellationToken);

            var message = envelope?.Error?.Message;

            return string.IsNullOrWhiteSpace(message) ? "sin detalle" : message;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return "sin detalle";
        }
    }

    private static Result<ProviderTranslation> MalformedResponse(string reason) =>
        Result.Failure<ProviderTranslation>(TranslationErrors.ProviderUnavailable(
            $"Respuesta inesperada del servicio de traducción: {reason}."));

    /// Configuración del HttpClient expuesta como método estático a propósito: así el
    /// registro en DI y los tests unitarios comparten exactamente el mismo cableado de
    /// cabeceras, en vez de que los tests validen una configuración que nadie usa.
    internal static void Configure(HttpClient client, AzureTranslatorOptions options)
    {
        client.BaseAddress = new Uri(options.Endpoint.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", options.ApiKey);
        client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Region", options.Region);
    }
}
