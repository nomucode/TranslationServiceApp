using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TranslationService.Application;
using TranslationService.Application.Abstractions.Messaging;
using TranslationService.Application.Translations.Commands.CreateTranslationJob;
using TranslationService.Application.Translations.Queries.GetTranslationJobById;
using TranslationService.Contracts.Translations;
using TranslationService.Domain.Translations.ValueObjects;
using TranslationService.Infrastructure;
using Xunit.Abstractions;

namespace TranslationService.Tests.Smoke;

/// Prueba de humo del flujo asíncrono completo contra Azure Translator REAL.
///
/// Monta el mismo contenedor que montará la Api (AddApplication + AddInfrastructure),
/// arranca el worker de verdad y recorre el ciclo entero: comando -> Pending -> cola ->
/// worker -> Azure -> estado terminal, consultado con la query igual que lo hará el GET.
/// Lo único que no interviene es la capa HTTP, que todavía no existe.
public sealed class AsynchronousFlowSmokeTests(ITestOutputHelper output) : IAsyncLifetime
{
    private ServiceProvider _services = null!;
    private IHostedService _worker = null!;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddApplication();
        services.AddInfrastructure(AzureSmokeConfiguration.Build());

        _services = services.BuildServiceProvider();

        if (!AzureSmokeConfiguration.HasCredentials)
        {
            return;
        }

        _worker = _services.GetServices<IHostedService>().Single();
        await _worker.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        if (_worker is not null)
        {
            await _worker.StopAsync(CancellationToken.None);
        }

        await _services.DisposeAsync();
    }

    private async Task<TranslationJobResponse> TranslateAndWaitAsync(string text)
    {
        await using var scope = _services.CreateAsyncScope();

        var createHandler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateTranslationJobCommand, JobId>>();

        var created = await createHandler.HandleAsync(new CreateTranslationJobCommand(text), CancellationToken.None);
        created.IsSuccess.Should().BeTrue("el comando debe aceptar el texto y devolver un JobId");

        var queryHandler = scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetTranslationJobByIdQuery, TranslationJobResponse>>();

        var query = new GetTranslationJobByIdQuery(created.Value.Value);

        // Sondeo con el mismo espíritu que hará el frontend: el estado inicial debe ser
        // no terminal y converger a Completed o Failed sin bloquear.
        var first = await queryHandler.HandleAsync(query, CancellationToken.None);
        first.Value.IsTerminal.Should().BeFalse("el POST debe devolver el control antes de traducir");

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var current = await queryHandler.HandleAsync(query, CancellationToken.None);
            if (current.Value.IsTerminal)
            {
                output.WriteLine(
                    $"[{current.Value.Status}] '{current.Value.SourceText}' -> '{current.Value.TranslatedText}' " +
                    $"(detectado: '{current.Value.DetectedLanguage}', traducido: {current.Value.WasTranslated}, " +
                    $"{current.Value.ProcessingTimeMs} ms)");

                return current.Value;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"El trabajo no alcanzó un estado terminal en 30 s para el texto '{text}'.");
    }

    [RequiresAzureCredentialsFact]
    public async Task EnglishText_ShouldBeTranslatedIntoSpanish()
    {
        var job = await TranslateAndWaitAsync("Good morning, how are you today?");

        job.Status.Should().Be(TranslationJobStatus.Completed, job.FailureReason);
        job.DetectedLanguage.Should().Be("en");
        job.WasTranslated.Should().BeTrue();
        job.TranslatedText.Should().NotBeEmpty().And.NotBe(job.SourceText);
    }

    [RequiresAzureCredentialsFact]
    public async Task SpanishText_ShouldBeReturnedUntouched()
    {
        // La regla de negocio, verificada contra la detección real de Azure.
        const string spanish = "Buenos días, ¿cómo estás hoy?";

        var job = await TranslateAndWaitAsync(spanish);

        job.Status.Should().Be(TranslationJobStatus.Completed, job.FailureReason);
        job.DetectedLanguage.Should().Be("es");
        job.WasTranslated.Should().BeFalse();
        job.TranslatedText.Should().Be(spanish);
    }

    [RequiresAzureCredentialsFact]
    public async Task ManyConcurrentJobs_ShouldAllReachATerminalState()
    {
        // Ejercita el paralelismo del worker y la contrapresión de la cola a la vez.
        string[] texts =
        [
            "Hello world", "Good evening", "Where is the station?",
            "Hola mundo", "Thank you very much", "See you tomorrow"
        ];

        var results = await Task.WhenAll(texts.Select(TranslateAndWaitAsync));

        results.Should().OnlyContain(job => job.Status == TranslationJobStatus.Completed);
        results.Single(job => job.SourceText == "Hola mundo").WasTranslated.Should().BeFalse();
    }
}
