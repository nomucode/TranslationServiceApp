using System.ComponentModel.DataAnnotations;

namespace TranslationService.Infrastructure.Translation;

public sealed class AzureTranslatorOptions
{
    public const string SectionName = "AzureTranslator";

    [Required(AllowEmptyStrings = false)]
    [Url]
    public string Endpoint { get; init; } = "https://api.cognitive.microsofttranslator.com";

    /// Nunca se persiste en appsettings.json: se inyecta con `dotnet user-secrets` en
    /// desarrollo y con una variable de entorno o Key Vault en despliegue.
    [Required(AllowEmptyStrings = false)]
    public string ApiKey { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string Region { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string ApiVersion { get; init; } = "3.0";

    public AzureTranslatorResilienceOptions Resilience { get; init; } = new();
}

/// Los parámetros de resiliencia son configuración, no constantes: permiten endurecer o
/// relajar el comportamiento en cada entorno sin recompilar.
public sealed class AzureTranslatorResilienceOptions
{
    [Range(0, 10)]
    public int MaxRetryAttempts { get; init; } = 3;

    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromMilliseconds(400);

    /// Proporción de fallos dentro de la ventana de muestreo que abre el circuito.
    [Range(0.1, 1.0)]
    public double CircuitBreakerFailureRatio { get; init; } = 0.5;

    public TimeSpan CircuitBreakerSamplingDuration { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan CircuitBreakerBreakDuration { get; init; } = TimeSpan.FromSeconds(15);

    /// Sin un mínimo de peticiones, un único fallo aislado abriría el circuito.
    [Range(2, 100)]
    public int CircuitBreakerMinimumThroughput { get; init; } = 4;

    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan TotalTimeout { get; init; } = TimeSpan.FromSeconds(45);
}
