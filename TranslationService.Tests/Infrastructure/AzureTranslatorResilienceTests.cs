using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TranslationService.Application.Abstractions.Translation;
using TranslationService.Domain.Translations.ValueObjects;
using TranslationService.Application;
using TranslationService.Infrastructure;

namespace TranslationService.Tests.Infrastructure;

/// Estos tests no ejercitan el adaptador aislado sino el pipeline real que construye
/// AddInfrastructure. Es la única forma de demostrar que Polly está efectivamente
/// enganchado al HttpClient y no meramente referenciado en el .csproj.
public sealed class AzureTranslatorResilienceTests
{
    /// Cuenta los intentos que llegan al transporte y siempre falla.
    private sealed class CountingFailureHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _attempts);

            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent("""{"error":{"code":500000,"message":"boom"}}"""),
                RequestMessage = request
            });
        }
    }

    private static ServiceProvider BuildProvider(
        HttpMessageHandler handler,
        Dictionary<string, string?> resilienceOverrides)
    {
        var settings = new Dictionary<string, string?>
        {
            ["AzureTranslator:Endpoint"] = "https://api.cognitive.microsofttranslator.com",
            ["AzureTranslator:ApiKey"] = "una-clave-de-prueba",
            ["AzureTranslator:Region"] = "westeurope",
            ["Translation:TargetLanguage"] = "es"
        };

        foreach (var (key, value) in resilienceOverrides)
        {
            settings[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddApplication();
        services.AddInfrastructure(configuration);
        // Se sustituye únicamente el transporte: todo el pipeline de resiliencia que
        // registró AddInfrastructure permanece intacto.
        services.ConfigureHttpClientDefaults(builder =>
            builder.ConfigurePrimaryHttpMessageHandler(() => handler));

        return services.BuildServiceProvider();
    }

    private static Task<TranslationService.Domain.Common.Result<ProviderTranslation>> TranslateAsync(IServiceProvider provider) =>
        provider.GetRequiredService<ITranslationProvider>()
            .TranslateAsync(
                SourceText.Create("Hello world").Value,
                LanguageCode.Spanish,
                CancellationToken.None);

    [Fact]
    public async Task Pipeline_ShouldRetryTransientServerErrors()
    {
        var handler = new CountingFailureHandler(HttpStatusCode.InternalServerError);
        await using var provider = BuildProvider(handler, new Dictionary<string, string?>
        {
            ["AzureTranslator:Resilience:MaxRetryAttempts"] = "3",
            ["AzureTranslator:Resilience:RetryBaseDelay"] = "00:00:00.005",
            // Se aparta el breaker para aislar el comportamiento del retry.
            ["AzureTranslator:Resilience:CircuitBreakerMinimumThroughput"] = "100"
        });

        var result = await TranslateAsync(provider);

        // 1 intento inicial + 3 reintentos.
        handler.Attempts.Should().Be(4);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Pipeline_ShouldNotRetryClientErrorsThatWillNeverSucceed()
    {
        // Un 401 por credenciales inválidas no es transitorio: reintentarlo sólo gasta
        // tiempo y cuota. Polly sólo debe reintentar lo que puede mejorar solo.
        var handler = new CountingFailureHandler(HttpStatusCode.Unauthorized);
        await using var provider = BuildProvider(handler, new Dictionary<string, string?>
        {
            ["AzureTranslator:Resilience:MaxRetryAttempts"] = "3",
            ["AzureTranslator:Resilience:RetryBaseDelay"] = "00:00:00.005",
            ["AzureTranslator:Resilience:CircuitBreakerMinimumThroughput"] = "100"
        });

        var result = await TranslateAsync(provider);

        handler.Attempts.Should().Be(1);
        result.IsFailure.Should().BeTrue();
        result.Error.Description.Should().Contain("401");
    }

    [Fact]
    public async Task Pipeline_ShouldOpenTheCircuitAndStopCallingADeadService()
    {
        var handler = new CountingFailureHandler(HttpStatusCode.ServiceUnavailable);
        await using var provider = BuildProvider(handler, new Dictionary<string, string?>
        {
            ["AzureTranslator:Resilience:MaxRetryAttempts"] = "0",
            ["AzureTranslator:Resilience:CircuitBreakerMinimumThroughput"] = "2",
            ["AzureTranslator:Resilience:CircuitBreakerFailureRatio"] = "0.5",
            ["AzureTranslator:Resilience:CircuitBreakerSamplingDuration"] = "00:00:30",
            ["AzureTranslator:Resilience:CircuitBreakerBreakDuration"] = "00:00:30"
        });

        for (var i = 0; i < 4; i++)
        {
            await TranslateAsync(provider);
        }

        var attemptsBeforeTheLastCall = handler.Attempts;
        var result = await TranslateAsync(provider);

        // Con el circuito abierto la llamada ni siquiera llega al transporte: se corta
        // rápido en lugar de seguir castigando a un servicio ya caído.
        handler.Attempts.Should().Be(attemptsBeforeTheLastCall);
        result.IsFailure.Should().BeTrue();
        result.Error.Description.Should().Contain("circuito abierto");
    }
}
