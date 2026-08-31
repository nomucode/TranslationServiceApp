using Microsoft.Extensions.Configuration;

namespace TranslationService.Tests.Smoke;

/// Marca un test que sólo tiene sentido con credenciales reales de Azure. Si no las hay,
/// se omite en lugar de fallar: así la suite sigue siendo verde en CI o en la máquina de
/// un revisor que no tenga clave, sin renunciar a poder validar la integración de verdad.
public sealed class RequiresAzureCredentialsFactAttribute : FactAttribute
{
    public RequiresAzureCredentialsFactAttribute()
    {
        if (!AzureSmokeConfiguration.HasCredentials)
        {
            Skip = "Sin credenciales de Azure Translator en user-secrets; test de integración omitido.";
        }
    }
}

public static class AzureSmokeConfiguration
{
    /// El UserSecretsId es el del proyecto Api: los tests leen exactamente los mismos
    /// secretos que usará la aplicación, en vez de una copia que podría divergir.
    private const string ApiUserSecretsId = "3e060b1b-7d86-4895-aec1-f6dd8a0eca77";

    public static IConfiguration Build() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Translation:TargetLanguage"] = "es",
            ["AzureTranslator:Endpoint"] = "https://api.cognitive.microsofttranslator.com",
            ["AzureTranslator:ApiVersion"] = "3.0",
            ["MessageQueue:MaxDegreeOfParallelism"] = "4"
        })
        .AddUserSecrets(ApiUserSecretsId)
        .AddEnvironmentVariables()
        .Build();

    public static bool HasCredentials
    {
        get
        {
            var configuration = Build();

            return !string.IsNullOrWhiteSpace(configuration["AzureTranslator:ApiKey"])
                && !string.IsNullOrWhiteSpace(configuration["AzureTranslator:Region"]);
        }
    }
}
