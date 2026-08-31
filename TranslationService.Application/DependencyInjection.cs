using Microsoft.Extensions.DependencyInjection;
using TranslationService.Application.Abstractions.Messaging;
using TranslationService.Application.Translations.Commands.CreateTranslationJob;
using TranslationService.Application.Translations.Commands.ProcessTranslationJob;
using TranslationService.Application.Translations.Queries.GetTranslationJobById;
using TranslationService.Contracts.Translations;
using TranslationService.Domain.Translations.ValueObjects;

namespace TranslationService.Application;

public static class DependencyInjection
{
    /// Registro explícito en lugar de escaneo por reflexión: son tres handlers, el coste de
    /// escribirlos es nulo y a cambio un handler mal cableado es un error de compilación
    /// en vez de un fallo en tiempo de ejecución.
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<
            ICommandHandler<CreateTranslationJobCommand, JobId>,
            CreateTranslationJobCommandHandler>();

        services.AddScoped<
            ICommandHandler<ProcessTranslationJobCommand>,
            ProcessTranslationJobCommandHandler>();

        services.AddScoped<
            IQueryHandler<GetTranslationJobByIdQuery, TranslationJobResponse>,
            GetTranslationJobByIdQueryHandler>();

        return services;
    }
}
