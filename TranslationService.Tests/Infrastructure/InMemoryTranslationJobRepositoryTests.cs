using TranslationService.Domain.Translations;
using TranslationService.Domain.Translations.ValueObjects;
using TranslationService.Infrastructure.Persistence;

namespace TranslationService.Tests.Infrastructure;

public sealed class InMemoryTranslationJobRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 10, 0, 0, TimeSpan.Zero);

    private readonly InMemoryTranslationJobRepository _sut = new();

    private static TranslationJob AJob(string text = "Hello world") =>
        TranslationJob.Request(SourceText.Create(text).Value, LanguageCode.Spanish, Now);

    [Fact]
    public async Task AddAsync_ShouldStoreTheJob()
    {
        var job = AJob();

        var added = await _sut.AddAsync(job);

        added.IsSuccess.Should().BeTrue();
        (await _sut.GetByIdAsync(job.Id)).Value.Should().BeSameAs(job);
    }

    [Fact]
    public async Task AddAsync_ShouldRejectADuplicateIdentifier()
    {
        var job = AJob();
        await _sut.AddAsync(job);

        var second = await _sut.AddAsync(job);

        second.IsFailure.Should().BeTrue();
        second.Error.Code.Should().Be("TranslationJob.Duplicate");
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReportNotFoundForAnUnknownJob()
    {
        var result = await _sut.GetByIdAsync(JobId.New());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("TranslationJob.NotFound");
    }

    [Fact]
    public async Task UpdateAsync_ShouldPersistTheNewState()
    {
        var job = AJob();
        await _sut.AddAsync(job);
        job.MarkAsProcessing();

        var updated = await _sut.UpdateAsync(job);

        updated.IsSuccess.Should().BeTrue();
        (await _sut.GetByIdAsync(job.Id)).Value.Status.Should().Be(TranslationStatus.Processing);
    }

    [Fact]
    public async Task UpdateAsync_ShouldRejectAJobThatWasNeverAdded()
    {
        var result = await _sut.UpdateAsync(AJob());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("TranslationJob.NotFound");
    }

    [Fact]
    public async Task Repository_ShouldTolerateConcurrentWriters()
    {
        // El escenario real: muchas peticiones HTTP creando trabajos mientras el worker
        // actualiza otros. Si el almacén no fuese concurrente, esto perdería escrituras.
        var jobs = Enumerable.Range(0, 200).Select(i => AJob($"texto {i}")).ToList();

        await Parallel.ForEachAsync(jobs, async (job, _) =>
        {
            await _sut.AddAsync(job);
            job.MarkAsProcessing();
            await _sut.UpdateAsync(job);
        });

        foreach (var job in jobs)
        {
            var stored = await _sut.GetByIdAsync(job.Id);
            stored.IsSuccess.Should().BeTrue();
            stored.Value.Status.Should().Be(TranslationStatus.Processing);
        }
    }
}
