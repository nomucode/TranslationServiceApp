using Scalar.AspNetCore;
using TranslationService.Api.Endpoints;
using TranslationService.Api.Infrastructure;
using TranslationService.Application;
using TranslationService.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Composition root: la Api es la única capa que conoce a todas las demás.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;
        // El identificador de traza es lo que permite correlacionar la respuesta que ve el
        // usuario con la línea concreta del log del servidor.
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    });

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
// Convierte también las respuestas de error sin cuerpo (404 de ruta, 405...) en ProblemDetails.
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Hosting del SPA: estos tres deben ir en este orden y después del pipeline de errores.
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.MapTranslationEndpoints();

// Cortafuegos entre la API y el SPA. Sin esto, MapFallbackToFile se traga cualquier ruta
// /api desconocida y devuelve el index.html con un 200: un cliente de API que se equivoca
// de ruta recibiría una página HTML en lugar de un error. Este catch-all tiene menos
// precedencia que los endpoints concretos, pero más que el fallback del SPA.
app.Map("/api/{**path}", (HttpContext context) => Results.Problem(
        title: "Recurso no encontrado",
        detail: $"No existe ningún endpoint para '{context.Request.Path}'.",
        statusCode: StatusCodes.Status404NotFound))
    .ExcludeFromDescription();

// Cualquier otra ruta se delega al enrutador de Blazor.
app.MapFallbackToFile("index.html");

app.Run();

/// Expuesto para que WebApplicationFactory pueda arrancar este mismo host en los tests de
/// integración, en lugar de replicar la configuración y arriesgarse a que diverja.
public partial class Program;
