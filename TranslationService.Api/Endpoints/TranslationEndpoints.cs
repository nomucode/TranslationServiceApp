using TranslationService.Api.Extensions;
using TranslationService.Application.Abstractions.Messaging;
using TranslationService.Application.Translations.Commands.CreateTranslationJob;
using TranslationService.Application.Translations.Queries.GetTranslationJobById;
using TranslationService.Contracts.Translations;
using TranslationService.Domain.Translations.ValueObjects;

namespace TranslationService.Api.Endpoints;

/// Las dos caras del Asynchronous Request-Reply: el endpoint que acepta trabajo y el que
/// informa de su estado. Los handlers se inyectan por su tipo cerrado, así que un cableado
/// incorrecto es un error de compilación y no un 500 en tiempo de ejecución.
internal static class TranslationEndpoints
{
    private const string RoutePrefix = "/api/translations";

    public static IEndpointRouteBuilder MapTranslationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix).WithTags("Translations");

        group.MapPost("/", CreateAsync)
            .WithName("CreateTranslation")
            .WithSummary("Encola un texto para traducir")
            .WithDescription(
                "Persiste el trabajo en estado Pending, encola el evento y devuelve el control " +
                "de inmediato. La traducción ocurre fuera del ciclo de la petición.")
            .Produces<TranslationAcceptedResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{jobId:guid}", GetAsync)
            .WithName("GetTranslation")
            .WithSummary("Consulta el estado de un trabajo de traducción")
            .Produces<TranslationJobResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        CreateTranslationRequest request,
        ICommandHandler<CreateTranslationJobCommand, JobId> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new CreateTranslationJobCommand(request.Text), cancellationToken);

        return result.Match(jobId =>
        {
            var statusUrl = $"{RoutePrefix}/{jobId.Value}";

            // 202 + Location: el contrato del patrón. El cliente recibe dónde sondear en
            // lugar de tener que componer la URL por su cuenta.
            return Results.Accepted(
                statusUrl,
                new TranslationAcceptedResponse(jobId.Value, TranslationJobStatus.Pending, statusUrl));
        });
    }

    private static async Task<IResult> GetAsync(
        Guid jobId,
        IQueryHandler<GetTranslationJobByIdQuery, TranslationJobResponse> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetTranslationJobByIdQuery(jobId), cancellationToken);

        return result.Match(response => Results.Ok(response));
    }
}
