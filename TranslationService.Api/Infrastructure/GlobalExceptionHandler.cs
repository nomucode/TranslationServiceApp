using Microsoft.AspNetCore.Diagnostics;
using TranslationService.Domain.Exceptions;

namespace TranslationService.Api.Infrastructure;

/// Red de seguridad final: cualquier excepción que llegue aquí sale como ProblemDetails
/// (RFC 7807), nunca como una página de error ni como un cuerpo vacío.
internal sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Una petición mal formada es ruido del cliente, no una incidencia del servidor:
        // registrarla como Error contaminaría las alertas de producción.
        var level = exception is BadHttpRequestException ? LogLevel.Warning : LogLevel.Error;

        logger.Log(
            level,
            exception,
            "Excepción no controlada procesando {Method} {Path}.",
            httpContext.Request.Method,
            httpContext.Request.Path);

        var (statusCode, title) = exception switch
        {
            // Una excepción de dominio que llegue hasta aquí significa que se intentó una
            // operación incoherente con el estado del recurso: es un 409, no un 500.
            DomainException => (StatusCodes.Status409Conflict, "Conflicto con el estado actual del recurso"),
            // Cuerpo mal formado o cabeceras inválidas: el propio framework ya ha decidido
            // el código correcto (400, 415...). Aplastarlo a 500 culparía al servidor de un
            // error que es del cliente.
            BadHttpRequestException badRequest => (badRequest.StatusCode, "La petición no es válida"),
            _ => (StatusCodes.Status500InternalServerError, "Se ha producido un error inesperado")
        };

        httpContext.Response.StatusCode = statusCode;

        var extensions = new Dictionary<string, object?>();
        if (exception is DomainException domainException)
        {
            extensions["code"] = domainException.Code;
        }

        // El detalle real sólo se expone en desarrollo: filtrar mensajes internos hacia
        // fuera es una fuga de información, no una ayuda al cliente.
        if (environment.IsDevelopment())
        {
            extensions["exception"] = exception.GetType().Name;
            extensions["stackTrace"] = exception.StackTrace;
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = statusCode,
                Title = title,
                Detail = environment.IsDevelopment()
                    ? exception.Message
                    : "Consulte los registros del servidor con el identificador de traza indicado.",
                Extensions = extensions
            }
        });
    }
}
