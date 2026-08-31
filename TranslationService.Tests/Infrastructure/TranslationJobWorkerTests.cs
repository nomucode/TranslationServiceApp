using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TranslationService.Application.Abstractions.Messaging;
using TranslationService.Application.Translations.Commands.ProcessTranslationJob;
using TranslationService.Domain.Common;
using TranslationService.Domain.Translations.Events;
using TranslationService.Domain.Translations.ValueObjects;
using TranslationService.Infrastructure.Messaging;

namespace TranslationService.Tests.Infrastructure;

public sealed class TranslationJobWorkerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 10, 0, 0, TimeSpan.Zero);

    /// Handler espía: registra los trabajos que recibe y permite guionizar el desenlace.
    private sealed class SpyHandler : ICommandHandler<ProcessTranslationJobCommand>
    {
        private readonly List<JobId> _handled = [];
        private readonly Func<JobId, Result> _behaviour;

        public SpyHandler(Func<JobId, Result>? behaviour = null) =>
            _behaviour = behaviour ?? (_ => Result.Success());

        public IReadOnlyList<JobId> Handled
        {
            get { lock (_handled) { return _handled.ToList(); } }
        }

        public Task<Result> HandleAsync(ProcessTranslationJobCommand command, CancellationToken cancellationToken)
        {
            lock (_handled) { _handled.Add(command.JobId); }
            return Task.FromResult(_behaviour(command.JobId));
        }
    }

    private static (TranslationJobWorker Worker, ChannelMessageQueue<TranslationRequestedEvent> Queue)
        CreateSut(SpyHandler handler, int maxDegreeOfParallelism = 4)
    {
        var options = Options.Create(new MessageQueueOptions
        {
            Capacity = 100,
            MaxDegreeOfParallelism = maxDegreeOfParallelism
        });

        var queue = new ChannelMessageQueue<TranslationRequestedEvent>(options);

        var services = new ServiceCollection()
            .AddScoped<ICommandHandler<ProcessTranslationJobCommand>>(_ => handler)
            .BuildServiceProvider();

        var worker = new TranslationJobWorker(
            queue,
            services.GetRequiredService<IServiceScopeFactory>(),
            options,
            NullLogger<TranslationJobWorker>.Instance);

        return (worker, queue);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !condition())
        {
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task Worker_ShouldProcessEveryEnqueuedJob()
    {
        var handler = new SpyHandler();
        var (worker, queue) = CreateSut(handler);
        var jobIds = Enumerable.Range(0, 25).Select(_ => JobId.New()).ToList();

        await worker.StartAsync(CancellationToken.None);
        foreach (var jobId in jobIds)
        {
            await queue.EnqueueAsync(new TranslationRequestedEvent(jobId, Now));
        }

        await WaitUntilAsync(() => handler.Handled.Count == jobIds.Count, TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        handler.Handled.Should().BeEquivalentTo(jobIds);
    }

    [Fact]
    public async Task Worker_ShouldKeepConsumingAfterAHandlerThrows()
    {
        // Desde .NET 6 una excepción que escape de un BackgroundService tumba el host.
        // Sin el catch del worker, un único mensaje defectuoso dejaría la cola muerta.
        var poisoned = JobId.New();
        var handler = new SpyHandler(jobId => jobId == poisoned
            ? throw new InvalidOperationException("mensaje envenenado")
            : Result.Success());

        var (worker, queue) = CreateSut(handler, maxDegreeOfParallelism: 1);

        await worker.StartAsync(CancellationToken.None);
        await queue.EnqueueAsync(new TranslationRequestedEvent(poisoned, Now));

        var healthy = JobId.New();
        await queue.EnqueueAsync(new TranslationRequestedEvent(healthy, Now));

        await WaitUntilAsync(() => handler.Handled.Contains(healthy), TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        handler.Handled.Should().Contain(healthy);
        worker.ExecuteTask!.IsFaulted.Should().BeFalse();
    }

    [Fact]
    public async Task Worker_ShouldKeepConsumingAfterAHandlerReturnsFailure()
    {
        var failing = JobId.New();
        var handler = new SpyHandler(jobId => jobId == failing
            ? Result.Failure(Error.Failure("Boom", "algo salió mal"))
            : Result.Success());

        var (worker, queue) = CreateSut(handler, maxDegreeOfParallelism: 1);

        await worker.StartAsync(CancellationToken.None);
        await queue.EnqueueAsync(new TranslationRequestedEvent(failing, Now));
        var healthy = JobId.New();
        await queue.EnqueueAsync(new TranslationRequestedEvent(healthy, Now));

        await WaitUntilAsync(() => handler.Handled.Contains(healthy), TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        handler.Handled.Should().Contain(healthy);
    }

    [Fact]
    public async Task Worker_ShouldShutDownCleanlyWhenTheHostStops()
    {
        // El apagado cancela el token y ReadAllAsync lanza OperationCanceledException:
        // debe quedarse en el catch del worker y no propagarse como fallo.
        var handler = new SpyHandler();
        var (worker, _) = CreateSut(handler);

        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        worker.ExecuteTask!.IsFaulted.Should().BeFalse();
        worker.ExecuteTask.IsCompleted.Should().BeTrue();
    }
}
