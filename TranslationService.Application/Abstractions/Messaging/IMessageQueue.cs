namespace TranslationService.Application.Abstractions.Messaging;

/// Puerto de la cola. Genérico a propósito: hoy sólo transporta TranslationRequestedEvent,
/// pero la firma no presupone nada del transporte, de modo que sustituir la implementación
/// basada en Channels por Service Bus o RabbitMQ no toca ni Application ni Domain.
public interface IMessageQueue<TMessage>
    where TMessage : class
{
    ValueTask EnqueueAsync(TMessage message, CancellationToken cancellationToken = default);

    /// Secuencia infinita que se completa sólo al cancelar el token: encaja de forma
    /// natural con el bucle de un BackgroundService.
    IAsyncEnumerable<TMessage> DequeueAllAsync(CancellationToken cancellationToken);
}
