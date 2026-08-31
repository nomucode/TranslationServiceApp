using System.Threading.Channels;
using Microsoft.Extensions.Options;
using TranslationService.Application.Abstractions.Messaging;

namespace TranslationService.Infrastructure.Messaging;

/// Implementación del puerto de cola sobre System.Threading.Channels.
///
/// Decisión de la prueba técnica: Channels evita levantar RabbitMQ o Service Bus sin
/// renunciar a la semántica productor/consumidor —contrapresión incluida—. Lo que se pierde
/// frente a un broker real es la durabilidad: si el proceso muere, los mensajes en vuelo se
/// pierden y sus trabajos quedan en Pending. En producción se sustituiría esta única clase,
/// porque ni Application ni Domain conocen Channels.
public sealed class ChannelMessageQueue<TMessage> : IMessageQueue<TMessage>
    where TMessage : class
{
    private readonly Channel<TMessage> _channel;

    public ChannelMessageQueue(IOptions<MessageQueueOptions> options) =>
        _channel = Channel.CreateBounded<TMessage>(new BoundedChannelOptions(options.Value.Capacity)
        {
            // Wait (y no DropWrite) porque perder una petición aceptada en silencio sería
            // peor que hacer esperar unos milisegundos al productor.
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });

    public ValueTask EnqueueAsync(TMessage message, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(message, cancellationToken);

    public IAsyncEnumerable<TMessage> DequeueAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
