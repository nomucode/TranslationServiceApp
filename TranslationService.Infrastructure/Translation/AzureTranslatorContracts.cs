using System.Text.Json.Serialization;

namespace TranslationService.Infrastructure.Translation;

/// Contratos de transporte de la API de Azure Translator v3. Los nullables son
/// inevitables aquí —es la frontera de deserialización, donde el JSON remoto puede omitir
/// cualquier campo— y por eso el adaptador los convierte en Result antes de dejarlos salir
/// hacia Application.
internal sealed record AzureTranslateRequestItem(
    [property: JsonPropertyName("Text")] string Text);

internal sealed record AzureDetectedLanguage(
    [property: JsonPropertyName("language")] string? Language,
    [property: JsonPropertyName("score")] double Score);

internal sealed record AzureTranslation(
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("to")] string? To);

internal sealed record AzureTranslateResultItem(
    [property: JsonPropertyName("detectedLanguage")] AzureDetectedLanguage? DetectedLanguage,
    [property: JsonPropertyName("translations")] IReadOnlyList<AzureTranslation>? Translations);

internal sealed record AzureErrorEnvelope(
    [property: JsonPropertyName("error")] AzureError? Error);

internal sealed record AzureError(
    [property: JsonPropertyName("code")] long Code,
    [property: JsonPropertyName("message")] string? Message);

/// Serialización con generación de código: sin reflexión en tiempo de ejecución, arranque
/// más rápido y compatible con recorte/AOT si el servicio se publicara así.
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AzureTranslateRequestItem[]))]
[JsonSerializable(typeof(IReadOnlyList<AzureTranslateResultItem>))]
[JsonSerializable(typeof(AzureErrorEnvelope))]
internal sealed partial class AzureTranslatorJsonContext : JsonSerializerContext;
