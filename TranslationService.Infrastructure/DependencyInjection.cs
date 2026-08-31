using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using TranslationService.Application.Abstractions.Messaging;
using TranslationService.Application.Abstractions.Translation;
using TranslationService.Application.Configuration;
using TranslationService.Domain.Repositories;
using TranslationService.Domain.Translations.Events;
using TranslationService.Infrastructure.Messaging;
using TranslationService.Infrastructure.Persistence;
using TranslationService.Infrastructure.Translation;

namespace TranslationService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptionsWithValidation<TranslationOptions>(configuration, TranslationOptions.SectionName);
        services.AddOptionsWithValidation<MessageQueueOptions>(configuration, MessageQueueOptions.SectionName);
        services.AddOptionsWithValidation<AzureTranslatorOptions>(configuration, AzureTranslatorOptions.SectionName);

        services.AddSingleton(TimeProvider.System);

        // Singleton porque el ConcurrentDictionary *es* el almacén: un scope por petición
        // haría que cada llamada viese un diccionario vacío.
        services.AddSingleton<ITranslationJobRepository, InMemoryTranslationJobRepository>();

        // La cola debe ser singleton por el mismo motivo: productor (Api) y consumidor
        // (worker) tienen que compartir exactamente la misma instancia de Channel.
        services.AddSingleton<IMessageQueue<TranslationRequestedEvent>,
            ChannelMessageQueue<TranslationRequestedEvent>>();

        services.AddHostedService<TranslationJobWorker>();

        services.AddAzureTranslator();

        return services;
    }

    private static IHttpResiliencePipelineBuilder AddAzureTranslator(this IServiceCollection services) =>
        services
            .AddHttpClient<ITranslationProvider, AzureTranslatorClient>(static (provider, client) =>
                AzureTranslatorClient.Configure(
                    client,
                    provider.GetRequiredService<IOptions<AzureTranslatorOptions>>().Value))
            .AddResilienceHandler("azure-translator", static (builder, context) =>
            {
                var options = context.ServiceProvider
                    .GetRequiredService<IOptions<AzureTranslatorOptions>>().Value.Resilience;

                // El orden de las estrategias es lo que define el comportamiento, y va de
                // fuera hacia dentro:
                //
                //   1. Timeout total  — techo absoluto de la operación, reintentos incluidos.
                //                       Sin él, 3 reintentos con backoff podrían encadenar
                //                       minutos y dejar el trabajo colgado.
                //   2. Retry          — absorbe fallos transitorios (5xx, 408, 429, red).
                //                       Va *fuera* del breaker para que cada reintento cuente
                //                       como una llamada más en la ventana de muestreo.
                //   3. Circuit breaker— si Azure está caído, deja de intentarlo y falla
                //                       rápido en lugar de castigar un servicio ya hundido.
                //   4. Timeout por intento — impide que una sola llamada colgada consuma
                //                       todo el presupuesto total.
                builder.AddTimeout(options.TotalTimeout);

                // Polly rechaza MaxRetryAttempts = 0, así que la ausencia de reintentos se
                // expresa omitiendo la estrategia en lugar de configurándola a cero. De este
                // modo "0" es una configuración válida y no un fallo de arranque.
                if (options.MaxRetryAttempts > 0)
                {
                    builder.AddRetry(new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = options.MaxRetryAttempts,
                        Delay = options.RetryBaseDelay,
                        BackoffType = DelayBackoffType.Exponential,
                        // Jitter: evita que varios trabajos que fallaron a la vez reintenten
                        // sincronizados y provoquen un pico contra un servicio ya frágil.
                        UseJitter = true
                    });
                }

                builder
                    .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                    {
                        FailureRatio = options.CircuitBreakerFailureRatio,
                        SamplingDuration = options.CircuitBreakerSamplingDuration,
                        MinimumThroughput = options.CircuitBreakerMinimumThroughput,
                        BreakDuration = options.CircuitBreakerBreakDuration
                    })
                    .AddTimeout(options.AttemptTimeout);
            });

    /// ValidateOnStart convierte una configuración incompleta —típicamente la API key sin
    /// poner— en un fallo de arranque inmediato y explícito, en lugar de en un 500 la
    /// primera vez que alguien intente traducir algo.
    private static IServiceCollection AddOptionsWithValidation<TOptions>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName)
        where TOptions : class
    {
        services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
