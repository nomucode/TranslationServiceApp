using Microsoft.Extensions.Time.Testing;
using TranslationService.Application.Translations.Queries.GetTranslationJobById;
using TranslationService.Contracts.Translations;
using TranslationService.Domain.Common;
using TranslationService.Domain.Repositories;
using TranslationService.Domain.Translations;
using TranslationService.Domain.Translations.Errors;
using TranslationService.Domain.Translations.ValueObjects;

namespace TranslationService.Tests.Application;

public sealed class GetTranslationJobByIdQueryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 10, 0, 0, TimeSpan.Zero);
    private static readonly LanguageCode English = LanguageCode.Create("en").Value;

    private readonly ITranslationJobRepository _repository = Substitute.For<ITranslationJobRepository>();

    private GetTranslationJobByIdQueryHandler CreateSut() => new(_repository);

    private TranslationJob Given(TranslationJob job)
    {
        _repository.GetByIdAsync(job.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(job));
        return job;
    }

    private static TranslationJob APendingJob(string text = "Hello world") =>
        TranslationJob.Request(SourceText.Create(text).Value, LanguageCode.Spanish, Now);

    [Fact]
    public async Task Handle_ShouldReportAPendingJobWithEmptyResultFields()
    {
        var job = Given(APendingJob());

        var result = await CreateSut().HandleAsync(new GetTranslationJobByIdQuery(job.Id.Value), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(TranslationJobStatus.Pending);
        result.Value.SourceText.Should().Be("Hello world");
        result.Value.TargetLanguage.Should().Be("es");
        result.Value.TranslatedText.Should().BeEmpty();
        result.Value.DetectedLanguage.Should().BeEmpty();
        result.Value.FailureReason.Should().BeEmpty();
        result.Value.ProcessingTimeMs.Should().Be(0);
        result.Value.IsTerminal.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ShouldProjectACompletedTranslation()
    {
        var job = APendingJob();
        job.MarkAsProcessing();
        job.CompleteAsTranslated(TranslatedText.Create("Hola mundo").Value, English, Now.AddMilliseconds(340));
        Given(job);

        var result = await CreateSut().HandleAsync(new GetTranslationJobByIdQuery(job.Id.Value), CancellationToken.None);

        result.Value.Status.Should().Be(TranslationJobStatus.Completed);
        result.Value.TranslatedText.Should().Be("Hola mundo");
        result.Value.DetectedLanguage.Should().Be("en");
        result.Value.WasTranslated.Should().BeTrue();
        result.Value.ProcessingTimeMs.Should().Be(340);
        result.Value.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldProjectAJobCompletedWithoutTranslation()
    {
        var job = APendingJob("Hola mundo");
        job.MarkAsProcessing();
        job.CompleteWithoutTranslation(LanguageCode.Spanish, Now);
        Given(job);

        var result = await CreateSut().HandleAsync(new GetTranslationJobByIdQuery(job.Id.Value), CancellationToken.None);

        result.Value.WasTranslated.Should().BeFalse();
        result.Value.TranslatedText.Should().Be("Hola mundo");
        result.Value.DetectedLanguage.Should().Be("es");
    }

    [Fact]
    public async Task Handle_ShouldProjectAFailedJob()
    {
        var job = APendingJob();
        job.Fail("Azure devolvió 503", Now.AddSeconds(1));
        Given(job);

        var result = await CreateSut().HandleAsync(new GetTranslationJobByIdQuery(job.Id.Value), CancellationToken.None);

        result.Value.Status.Should().Be(TranslationJobStatus.Failed);
        result.Value.FailureReason.Should().Be("Azure devolvió 503");
        result.Value.TranslatedText.Should().BeEmpty();
        result.Value.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldReturnNotFoundForAnUnknownJob()
    {
        _repository.GetByIdAsync(Arg.Any<JobId>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<TranslationJob>(TranslationErrors.JobNotFound));

        var result = await CreateSut().HandleAsync(new GetTranslationJobByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_ShouldRejectAMalformedIdentifierWithoutQueryingTheRepository()
    {
        var result = await CreateSut().HandleAsync(new GetTranslationJobByIdQuery(Guid.Empty), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        await _repository.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }
}
