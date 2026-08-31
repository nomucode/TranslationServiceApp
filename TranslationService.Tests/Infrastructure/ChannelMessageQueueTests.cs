using Microsoft.Extensions.Options;
using TranslationService.Domain.Translations.Events;
using TranslationService.Domain.Translations.ValueObjects;
using TranslationService.Infrastructure.Messaging;

namespace TranslationService.Tests.Infrastructure;

public sealed class ChannelMessageQueueTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 10, 0, 0, TimeSpan.Zero);

    private static ChannelMessageQueue<TranslationRequestedEvent> CreateSut(int capacity = 100) =>
        new(Options.Create(new MessageQueueOptions { Capacity = capacity }));

    private static TranslationRequestedEvent AnEvent() => new(JobId.New(), Now);

    [Fact]
    public async Task Queue_ShouldDeliverEnqueuedMessagesInOrder()
    {
        var sut = CreateSut();
        var sent = Enumerable.Range(0, 5).Select(_ => AnEvent()).ToList();
        foreach (var message in sent)
        {
            await sut.EnqueueAsync(message);
        }

        var received = new List<TranslationRequestedEvent>();
        await foreach (var message in sut.DequeueAllAsync(CancellationToken.None))
        {
            received.Add(message);
            if (received.Count == sent.Count)
            {
                // Se sale con break y no cancelando: cancelar hace que ReadAllAsync lance,
                // que es justo por lo que el worker envuelve su bucle en un catch.
                break;
            }
        }

        received.Should().Equal(sent);
    }

    [Fact]
    public async Task DequeueAllAsync_ShouldWaitInsteadOfCompletingWhenTheQueueIsEmpty()
    {
        // Es la propiedad que permite al worker quedarse esperando trabajo indefinidamente
        // sin hacer polling ni consumir CPU.
        var sut = CreateSut();
        using var cts = new CancellationTokenSource();

        var consumer = Task.Run(async () =>
        {
            await foreach (var _ in sut.DequeueAllAsync(cts.Token)) { }
        }, CancellationToken.None);

        var finishedOnItsOwn = await Task.WhenAny(consumer, Task.Delay(150)) == consumer;
        await cts.CancelAsync();

        finishedOnItsOwn.Should().BeFalse();
    }

    [Fact]
    public async Task EnqueueAsync_ShouldApplyBackpressureWhenTheQueueIsFull()
    {
        // Cola acotada con FullMode.Wait: al llenarse, el productor espera en vez de
        // descartar mensajes en silencio o crecer sin límite en memoria.
        var sut = CreateSut(capacity: 1);
        await sut.EnqueueAsync(AnEvent());

        var blocked = sut.EnqueueAsync(AnEvent()).AsTask();
        var completedWhileFull = await Task.WhenAny(blocked, Task.Delay(150)) == blocked;

        completedWhileFull.Should().BeFalse();

        // Al liberar un hueco, el productor bloqueado debe poder avanzar.
        using var cts = new CancellationTokenSource();
        await using var reader = sut.DequeueAllAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        await reader.MoveNextAsync();

        await blocked.WaitAsync(TimeSpan.FromSeconds(2));
        blocked.IsCompletedSuccessfully.Should().BeTrue();
        await cts.CancelAsync();
    }

    [Fact]
    public async Task EnqueueAsync_ShouldHonourCancellation()
    {
        var sut = CreateSut(capacity: 1);
        await sut.EnqueueAsync(AnEvent());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var enqueue = async () => await sut.EnqueueAsync(AnEvent(), cts.Token);

        await enqueue.Should().ThrowAsync<OperationCanceledException>();
    }
}
