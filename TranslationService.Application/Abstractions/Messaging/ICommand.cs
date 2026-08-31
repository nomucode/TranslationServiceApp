using TranslationService.Domain.Common;

namespace TranslationService.Application.Abstractions.Messaging;

/// Marcador que ata un comando a su tipo de retorno: el compilador impide luego registrar
/// o inyectar un handler con la firma equivocada.
public interface ICommand;

public interface ICommand<TResult>;

public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    Task<Result> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken);
}
