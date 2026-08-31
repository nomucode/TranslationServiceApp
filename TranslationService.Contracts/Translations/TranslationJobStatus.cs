using System.Text.Json.Serialization;

namespace TranslationService.Contracts.Translations;

/// El converter va en el propio enum (y no en la configuración del host) para que Api y
/// cliente WASM serialicen igual sin tener que acordar opciones de JSON por separado.
[JsonConverter(typeof(JsonStringEnumConverter<TranslationJobStatus>))]
public enum TranslationJobStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}
