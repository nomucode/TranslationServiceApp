using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TranslationService.Application.Abstractions.Translation;

namespace TranslationService.Tests.Api;

/// Arranca el host real de la Api —el mismo Program.cs— y sustituye únicamente el
/// adaptador de Azure. Así los tests cubren el pipeline auténtico (ProblemDetails, worker,
/// cola, hosting del SPA) sin depender de la red ni de credenciales.
internal sealed class TranslationApiFactory : WebApplicationFactory<Program>
{
    public StubTranslationProvider Translator { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configuration) =>
            // Valores ficticios: AzureTranslatorOptions exige ApiKey y Region y los valida
            // al arrancar. Nunca se usan porque el proveedor está sustituido.
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureTranslator:ApiKey"] = "clave-de-test",
                ["AzureTranslator:Region"] = "region-de-test",
                ["Translation:TargetLanguage"] = "es"
            }));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ITranslationProvider>();
            services.AddSingleton<ITranslationProvider>(Translator);
        });
    }
}
