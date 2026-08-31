using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TranslationService.Application.Abstractions.Messaging;
using TranslationService.Application.Configuration;
using TranslationService.Application.Translations.Commands.CreateTranslationJob;
using TranslationService.Domain.Common;
using TranslationService.Domain.Repositories;
using TranslationService.Domain.Translations;
using TranslationService.Domain.Translations.Events;
using TranslationService.Domain.Translations.ValueObjects;

namespace TranslationService.Tests.Application;

public sealed class CreateTranslationJobCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 10, 0, 0, TimeSpan.Zero);

    private readonly ITranslationJobRepository _repository = Substitute.For<ITranslationJobRepository>();
    private readonly IMessageQueue<TranslationRequestedEvent> _queue =
        Substitute.For<IMessageQueue<TranslationRequestedEvent>>();
    private readonly FakeTimeProvider _time = new(Now);

    private CreateTranslationJobCommandHandler CreateSut(string targetLanguage = "es")
    {
        _repository.AddAsync(Arg.Any<TranslationJob>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        return new CreateTranslationJobCommandHandler(
            _repository,
            _queue,
            Options.Create(new TranslationOptions { TargetLanguage = targetLanguage }),
            _time,
            NullLogger<CreateTranslationJobCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_ShouldPersistAPendingJobAndReturnItsId()
    {
        var sut = CreateSut();

        var result = await sut.HandleAsync(new CreateTranslationJobCommand("Hello world"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _repository.Received(1).AddAsync(
            Arg.Is<TranslationJob>(job =>
                job.Status == TranslationStatus.Pending &&
                job.SourceText.Value == "Hello world" &&
                job.Id == result.Value),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldStampTheJobWithTheProvidedClock()
    {
        var sut = CreateSut();

        await sut.HandleAsync(new CreateTranslationJobCommand("Hello"), CancellationToken.None);

        await _repository.Received(1).AddAsync(
            Arg.Is<TranslationJob>(job => job.RequestedAt == Now),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldApplyTheConfiguredTargetLanguage()
    {
        var sut = CreateSut(targetLanguage: "es");

        await sut.HandleAsync(new CreateTranslationJobCommand("Hello"), CancellationToken.None);

        await _repository.Received(1).AddAsync(
            Arg.Is<TranslationJob>(job => job.TargetLanguage == LanguageCode.Spanish),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldEnqueueTheEventCarryingTheNewJobId()
    {
        var sut = CreateSut();

        var result = await sut.HandleAsync(new CreateTranslationJobCommand("Hello world"), CancellationToken.None);

        await _queue.Received(1).EnqueueAsync(
            Arg.Is<TranslationRequestedEvent>(e => e.JobId == result.Value && e.OccurredAt == Now),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_ShouldRejectBlankTextWithoutTouchingTheRepository(string text)
    {
        var sut = CreateSut();

        var result = await sut.HandleAsync(new CreateTranslationJobCommand(text), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _queue.DidNotReceiveWithAnyArgs().EnqueueAsync(default!, default);
    }

    [Fact]
    public async Task Handle_ShouldRejectTextLongerThanTheAzureLimit()
    {
        var sut = CreateSut();

        var result = await sut.HandleAsync(
            new CreateTranslationJobCommand(new string('a', SourceText.MaxLength + 1)),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await _queue.DidNotReceiveWithAnyArgs().EnqueueAsync(default!, default);
    }

    [Fact]
    public async Task Handle_ShouldNotEnqueueWhenPersistenceFails()
    {
        // Si se encolara igualmente, el worker despertaría para un job que no existe.
        // El orden persistir-luego-encolar es lo que hace el flujo asíncrono consistente.
        var sut = CreateSut();
        _repository.AddAsync(Arg.Any<TranslationJob>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(Error.Failure("Repo.Down", "sin almacenamiento")));

        var result = await sut.HandleAsync(new CreateTranslationJobCommand("Hello"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await _queue.DidNotReceiveWithAnyArgs().EnqueueAsync(default!, default);
    }

    [Fact]
    public async Task Handle_ShouldFailWhenTheConfiguredTargetLanguageIsInvalid()
    {
        var sut = CreateSut(targetLanguage: "no-es-un-idioma!");

        var result = await sut.HandleAsync(new CreateTranslationJobCommand("Hello"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }
}
