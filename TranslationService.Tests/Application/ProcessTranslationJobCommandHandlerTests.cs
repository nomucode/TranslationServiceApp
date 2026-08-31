using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TranslationService.Application.Abstractions.Translation;
using TranslationService.Application.Translations.Commands.ProcessTranslationJob;
using TranslationService.Domain.Common;
using TranslationService.Domain.Repositories;
using TranslationService.Domain.Translations;
using TranslationService.Domain.Translations.Errors;
using TranslationService.Domain.Translations.ValueObjects;

namespace TranslationService.Tests.Application;

public sealed class ProcessTranslationJobCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 10, 0, 0, TimeSpan.Zero);
    private static readonly LanguageCode English = LanguageCode.Create("en").Value;

    private readonly ITranslationJobRepository _repository = Substitute.For<ITranslationJobRepository>();
    private readonly ITranslationProvider _provider = Substitute.For<ITranslationProvider>();
    private readonly FakeTimeProvider _time = new(Now);

    private ProcessTranslationJobCommandHandler CreateSut() => new(
        _repository,
        _provider,
        _time,
        NullLogger<ProcessTranslationJobCommandHandler>.Instance);

    private TranslationJob GivenAPendingJob(string text = "Hello world")
    {
        var job = TranslationJob.Request(SourceText.Create(text).Value, LanguageCode.Spanish, Now);
        _repository.GetByIdAsync(job.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(job));
        _repository.UpdateAsync(Arg.Any<TranslationJob>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        return job;
    }

    private void GivenTheProviderReturns(string translated, LanguageCode detected) =>
        _provider.TranslateAsync(Arg.Any<SourceText>(), Arg.Any<LanguageCode>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new ProviderTranslation(TranslatedText.Create(translated).Value, detected)));

    // ---------- Camino feliz ----------

    [Fact]
    public async Task Handle_ShouldTranslateAndCompleteTheJob()
    {
        var job = GivenAPendingJob();
        GivenTheProviderReturns("Hola mundo", English);
        var sut = CreateSut();

        var result = await sut.HandleAsync(new ProcessTranslationJobCommand(job.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        job.Status.Should().Be(TranslationStatus.Completed);
        job.Outcome.Value.Text.Value.Should().Be("Hola mundo");
        job.Outcome.Value.DetectedLanguage.Should().Be(English);
        job.Outcome.Value.WasTranslated.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldMarkTheJobAsProcessingBeforeCallingTheProvider()
    {
        // Sin esta persistencia intermedia, un GET durante una traducción lenta seguiría
        // reportando 'Pending' y el usuario no sabría que su petición avanza.
        var job = GivenAPendingJob();
        var statusWhenProviderWasCalled = TranslationStatus.Failed;
        _provider.TranslateAsync(Arg.Any<SourceText>(), Arg.Any<LanguageCode>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                statusWhenProviderWasCalled = job.Status;
                return Result.Success(new ProviderTranslation(TranslatedText.Create("Hola").Value, English));
            });

        await CreateSut().HandleAsync(new ProcessTranslationJobCommand(job.Id), CancellationToken.None);

        statusWhenProviderWasCalled.Should().Be(TranslationStatus.Processing);
        await _repository.Received(2).UpdateAsync(job, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldForwardTheJobSourceTextAndTargetLanguageToTheProvider()
    {
        var job = GivenAPendingJob("Good morning");
        GivenTheProviderReturns("Buenos días", English);

        await CreateSut().HandleAsync(new ProcessTranslationJobCommand(job.Id), CancellationToken.None);

        await _provider.Received(1).TranslateAsync(
            Arg.Is<SourceText>(t => t.Value == "Good morning"),
            LanguageCode.Spanish,
            Arg.Any<CancellationToken>());
    }

    // ---------- La regla de negocio del idioma 'es' ----------

    [Fact]
    public async Task Handle_ShouldNotApplyTheTranslationWhenTheDetectedLanguageIsTheTarget()
    {
        const string spanish = "Hola, ¿qué tal?";
        var job = GivenAPendingJob(spanish);
        // Azure traduce igualmente y devuelve algo; el dominio debe descartarlo.
        GivenTheProviderReturns("Hello, how are you?", LanguageCode.Spanish);

        var result = await CreateSut().HandleAsync(new ProcessTranslationJobCommand(job.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        job.Status.Should().Be(TranslationStatus.Completed);
        job.Outcome.Value.WasTranslated.Should().BeFalse();
        job.Outcome.Value.Text.Value.Should().Be(spanish);
    }

    [Fact]
    public async Task Handle_ShouldTreatTheDetectedLanguageCaseInsensitively()
    {
        var job = GivenAPendingJob("Hola mundo");
        GivenTheProviderReturns("Hello world", LanguageCode.Create("ES").Value);

        await CreateSut().HandleAsync(new ProcessTranslationJobCommand(job.Id), CancellationToken.None);

        job.Outcome.Value.WasTranslated.Should().BeFalse();
    }

    // ---------- Caminos de fallo ----------

    [Fact]
    public async Task Handle_ShouldFailTheJobWhenTheProviderIsUnavailable()
    {
        var job = GivenAPendingJob();
        _provider.TranslateAsync(Arg.Any<SourceText>(), Arg.Any<LanguageCode>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ProviderTranslation>(
                TranslationErrors.ProviderUnavailable("Azure devolvió 503")));

        // El caso de uso concluyó correctamente: el desenlace 'Failed' quedó registrado.
        var result = await CreateSut().HandleAsync(new ProcessTranslationJobCommand(job.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        job.Status.Should().Be(TranslationStatus.Failed);
        job.FailureReason.Value.Should().Contain("503");
    }

    [Fact]
    public async Task Handle_ShouldNeverLeaveAJobStuckInProcessingWhenTheProviderThrows()
    {
        var job = GivenAPendingJob();
        _provider.TranslateAsync(Arg.Any<SourceText>(), Arg.Any<LanguageCode>(), Arg.Any<CancellationToken>())
            .Returns<Result<ProviderTranslation>>(_ => throw new InvalidOperationException("boom"));

        var result = await CreateSut().HandleAsync(new ProcessTranslationJobCommand(job.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        job.Status.Should().Be(TranslationStatus.Failed);
    }

    [Fact]
    public async Task Handle_ShouldReturnFailureWhenTheJobDoesNotExist()
    {
        var unknown = JobId.New();
        _repository.GetByIdAsync(unknown, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<TranslationJob>(TranslationErrors.JobNotFound));

        var result = await CreateSut().HandleAsync(new ProcessTranslationJobCommand(unknown), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        await _provider.DidNotReceiveWithAnyArgs().TranslateAsync(default!, default!, default);
    }

    [Fact]
    public async Task Handle_ShouldBeIdempotentForAJobThatIsNoLongerPending()
    {
        // Una cola en memoria puede reentregar; procesar dos veces no debe explotar
        // ni sobrescribir un desenlace ya registrado.
        var job = GivenAPendingJob();
        GivenTheProviderReturns("Hola mundo", English);
        var sut = CreateSut();
        await sut.HandleAsync(new ProcessTranslationJobCommand(job.Id), CancellationToken.None);

        var second = await sut.HandleAsync(new ProcessTranslationJobCommand(job.Id), CancellationToken.None);

        second.IsSuccess.Should().BeTrue();
        job.Status.Should().Be(TranslationStatus.Completed);
        await _provider.Received(1).TranslateAsync(
            Arg.Any<SourceText>(), Arg.Any<LanguageCode>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldRecordTheCompletionTimestampFromTheClock()
    {
        var job = GivenAPendingJob();
        GivenTheProviderReturns("Hola mundo", English);
        _provider.TranslateAsync(Arg.Any<SourceText>(), Arg.Any<LanguageCode>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _time.Advance(TimeSpan.FromMilliseconds(340));
                return Result.Success(new ProviderTranslation(TranslatedText.Create("Hola mundo").Value, English));
            });

        await CreateSut().HandleAsync(new ProcessTranslationJobCommand(job.Id), CancellationToken.None);

        job.CompletedAt.Value.Should().Be(Now.AddMilliseconds(340));
    }
}
