using TranslationService.Domain.Exceptions;
using TranslationService.Domain.Translations;
using TranslationService.Domain.Translations.ValueObjects;

namespace TranslationService.Tests.Domain.Translations;

public sealed class TranslationJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 10, 0, 0, TimeSpan.Zero);
    private static readonly LanguageCode English = LanguageCode.Create("en").Value;

    private static TranslationJob APendingJob(string text = "Hello world") =>
        TranslationJob.Request(SourceText.Create(text).Value, LanguageCode.Spanish, Now);

    private static TranslationJob AProcessingJob(string text = "Hello world")
    {
        var job = APendingJob(text);
        job.MarkAsProcessing();
        return job;
    }

    private static TranslatedText ATranslation(string value = "Hola mundo") =>
        TranslatedText.Create(value).Value;

    // ---------- Creación ----------

    [Fact]
    public void Request_ShouldCreateThejobInPendingState()
    {
        var job = APendingJob();

        job.Status.Should().Be(TranslationStatus.Pending);
        job.Id.Value.Should().NotBe(Guid.Empty);
        job.RequestedAt.Should().Be(Now);
        job.TargetLanguage.Should().Be(LanguageCode.Spanish);
    }

    [Fact]
    public void Request_ShouldNotExposeAnOutcomeYet()
    {
        var job = APendingJob();

        job.Outcome.IsFailure.Should().BeTrue();
        job.CompletedAt.IsFailure.Should().BeTrue();
        job.FailureReason.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Request_ShouldAssignAUniqueIdToEachJob()
    {
        APendingJob().Id.Should().NotBe(APendingJob().Id);
    }

    // ---------- Pending -> Processing ----------

    [Fact]
    public void MarkAsProcessing_ShouldMoveAPendingJobToProcessing()
    {
        var job = APendingJob();

        job.MarkAsProcessing();

        job.Status.Should().Be(TranslationStatus.Processing);
    }

    [Fact]
    public void MarkAsProcessing_ShouldRejectAJobAlreadyBeingProcessed()
    {
        // Protege contra el doble consumo del mismo mensaje de la cola.
        var job = AProcessingJob();

        var transition = job.MarkAsProcessing;

        transition.Should().Throw<InvalidJobStateTransitionException>();
    }

    [Fact]
    public void MarkAsProcessing_ShouldRejectACompletedJob()
    {
        var job = AProcessingJob();
        job.CompleteAsTranslated(ATranslation(), English, Now);

        var transition = job.MarkAsProcessing;

        transition.Should().Throw<InvalidJobStateTransitionException>();
    }

    // ---------- Processing -> Completed (traducido) ----------

    [Fact]
    public void CompleteAsTranslated_ShouldStoreTheTranslationAndTheDetectedLanguage()
    {
        var job = AProcessingJob();
        var completedAt = Now.AddSeconds(2);

        job.CompleteAsTranslated(ATranslation(), English, completedAt);

        job.Status.Should().Be(TranslationStatus.Completed);
        job.Outcome.IsSuccess.Should().BeTrue();
        job.Outcome.Value.Text.Value.Should().Be("Hola mundo");
        job.Outcome.Value.DetectedLanguage.Should().Be(English);
        job.Outcome.Value.WasTranslated.Should().BeTrue();
        job.CompletedAt.Value.Should().Be(completedAt);
    }

    [Fact]
    public void CompleteAsTranslated_ShouldRejectAJobThatIsStillPending()
    {
        var job = APendingJob();

        var transition = () => job.CompleteAsTranslated(ATranslation(), English, Now);

        transition.Should().Throw<InvalidJobStateTransitionException>();
    }

    [Fact]
    public void CompleteAsTranslated_ShouldRejectWhenDetectedLanguageEqualsTarget()
    {
        // Invariante de negocio: si Azure detecta el idioma destino, el camino correcto es
        // CompleteWithoutTranslation. Permitir lo contrario falsearía WasTranslated.
        var job = AProcessingJob("Hola mundo");

        var transition = () => job.CompleteAsTranslated(ATranslation(), LanguageCode.Spanish, Now);

        transition.Should().Throw<InvalidJobStateTransitionException>();
    }

    [Fact]
    public void CompleteAsTranslated_ShouldRejectBeingAppliedTwice()
    {
        var job = AProcessingJob();
        job.CompleteAsTranslated(ATranslation(), English, Now);

        var transition = () => job.CompleteAsTranslated(ATranslation(), English, Now);

        transition.Should().Throw<InvalidJobStateTransitionException>();
    }

    // ---------- Processing -> Completed (sin traducir: la regla del 'es') ----------

    [Fact]
    public void CompleteWithoutTranslation_ShouldReturnTheSourceTextUntouched()
    {
        const string spanishText = "Hola, ¿qué tal estás?";
        var job = AProcessingJob(spanishText);

        job.CompleteWithoutTranslation(LanguageCode.Spanish, Now);

        job.Status.Should().Be(TranslationStatus.Completed);
        job.Outcome.Value.Text.Value.Should().Be(spanishText);
        job.Outcome.Value.WasTranslated.Should().BeFalse();
        job.Outcome.Value.DetectedLanguage.Should().Be(LanguageCode.Spanish);
    }

    [Fact]
    public void CompleteWithoutTranslation_ShouldRejectWhenDetectedLanguageDiffersFromTarget()
    {
        var job = AProcessingJob();

        var transition = () => job.CompleteWithoutTranslation(English, Now);

        transition.Should().Throw<InvalidJobStateTransitionException>();
    }

    [Fact]
    public void CompleteWithoutTranslation_ShouldRejectAPendingJob()
    {
        var job = APendingJob("Hola mundo");

        var transition = () => job.CompleteWithoutTranslation(LanguageCode.Spanish, Now);

        transition.Should().Throw<InvalidJobStateTransitionException>();
    }

    // ---------- -> Failed ----------

    [Fact]
    public void Fail_ShouldRecordTheReason()
    {
        var job = AProcessingJob();

        job.Fail("Azure devolvió 503", Now);

        job.Status.Should().Be(TranslationStatus.Failed);
        job.FailureReason.Value.Should().Be("Azure devolvió 503");
        job.Outcome.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Fail_ShouldBeAllowedDirectlyFromPending()
    {
        // El worker puede fallar antes incluso de empezar (p. ej. circuito abierto).
        var job = APendingJob();

        job.Fail("Circuito abierto", Now);

        job.Status.Should().Be(TranslationStatus.Failed);
    }

    [Fact]
    public void Fail_ShouldRejectACompletedJob()
    {
        var job = AProcessingJob();
        job.CompleteAsTranslated(ATranslation(), English, Now);

        var transition = () => job.Fail("tarde", Now);

        transition.Should().Throw<InvalidJobStateTransitionException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Fail_ShouldFallBackToAGenericReasonWhenNoneIsGiven(string? reason)
    {
        var job = AProcessingJob();

        job.Fail(reason!, Now);

        job.FailureReason.Value.Should().NotBeNullOrWhiteSpace();
    }

    // ---------- Consistencia general ----------

    [Fact]
    public void TerminalStates_ShouldNotAcceptAnyFurtherTransition()
    {
        var completed = AProcessingJob();
        completed.CompleteAsTranslated(ATranslation(), English, Now);

        var failed = AProcessingJob();
        failed.Fail("boom", Now);

        foreach (var job in new[] { completed, failed })
        {
            ((Action)job.MarkAsProcessing).Should().Throw<InvalidJobStateTransitionException>();
            ((Action)(() => job.Fail("x", Now))).Should().Throw<InvalidJobStateTransitionException>();
            ((Action)(() => job.CompleteAsTranslated(ATranslation(), English, Now)))
                .Should().Throw<InvalidJobStateTransitionException>();
        }
    }

    [Fact]
    public void InvalidTransition_ShouldReportOriginTargetAndJobId()
    {
        var job = APendingJob();

        var exception = Assert.Throws<InvalidJobStateTransitionException>(
            () => job.CompleteAsTranslated(ATranslation(), English, Now));

        exception.JobId.Should().Be(job.Id.Value);
        exception.From.Should().Be(TranslationStatus.Pending);
        exception.To.Should().Be(TranslationStatus.Completed);
    }
}
