using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TranslationService.Application.Abstractions.Translation;
using TranslationService.Domain.Translations.ValueObjects;
using TranslationService.Infrastructure.Translation;

namespace TranslationService.Tests.Infrastructure;

public sealed class AzureTranslatorClientTests
{
    private const string SuccessBody = """
        [{
          "detectedLanguage": { "language": "en", "score": 1.0 },
          "translations": [{ "text": "Hola mundo", "to": "es" }]
        }]
        """;

    private static readonly AzureTranslatorOptions Options = new()
    {
        Endpoint = "https://api.cognitive.microsofttranslator.com",
        ApiKey = "una-clave-de-prueba",
        Region = "westeurope",
        ApiVersion = "3.0"
    };

    private static (ITranslationProvider Provider, StubHttpMessageHandler Handler) CreateSut(
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string body = SuccessBody,
        string contentType = "application/json")
    {
        var handler = new StubHttpMessageHandler(statusCode, body, contentType);
        var httpClient = new HttpClient(handler);
        // Se reutiliza el mismo cableado que usa el registro en DI, de modo que el test
        // valida la configuración real y no una réplica que podría divergir.
        AzureTranslatorClient.Configure(httpClient, Options);

        var provider = new AzureTranslatorClient(
            httpClient,
            Microsoft.Extensions.Options.Options.Create(Options),
            NullLogger<AzureTranslatorClient>.Instance);

        return (provider, handler);
    }

    private static Task<TranslationService.Domain.Common.Result<ProviderTranslation>> TranslateAsync(
        ITranslationProvider provider,
        string text = "Hello world") =>
        provider.TranslateAsync(SourceText.Create(text).Value, LanguageCode.Spanish, CancellationToken.None);

    // ---------- Forma de la petición ----------

    [Fact]
    public async Task TranslateAsync_ShouldCallTheTranslateEndpointOmittingTheSourceLanguage()
    {
        var (provider, handler) = CreateSut();

        await TranslateAsync(provider);

        var uri = handler.LastRequest.RequestUri!;
        uri.AbsolutePath.Should().Be("/translate");
        uri.Query.Should().Contain("api-version=3.0").And.Contain("to=es");
        // La ausencia de 'from' es lo que hace que Azure autodetecte el idioma.
        uri.Query.Should().NotContain("from=");
        handler.LastRequest.Method.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public async Task TranslateAsync_ShouldSendTheSubscriptionHeaders()
    {
        var (provider, handler) = CreateSut();

        await TranslateAsync(provider);

        handler.LastRequest.Headers.GetValues("Ocp-Apim-Subscription-Key")
            .Should().ContainSingle().Which.Should().Be("una-clave-de-prueba");
        handler.LastRequest.Headers.GetValues("Ocp-Apim-Subscription-Region")
            .Should().ContainSingle().Which.Should().Be("westeurope");
    }

    [Fact]
    public async Task TranslateAsync_ShouldSendTheTextInTheArrayShapeAzureExpects()
    {
        var (provider, handler) = CreateSut();

        await TranslateAsync(provider, "Hello world");

        using var document = JsonDocument.Parse(handler.LastRequestBody!);
        document.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        document.RootElement.GetArrayLength().Should().Be(1);
        document.RootElement[0].GetProperty("Text").GetString().Should().Be("Hello world");
    }

    // ---------- Lectura de la respuesta ----------

    [Fact]
    public async Task TranslateAsync_ShouldReturnTheTranslationAndTheDetectedLanguage()
    {
        var (provider, _) = CreateSut();

        var result = await TranslateAsync(provider);

        result.IsSuccess.Should().BeTrue();
        result.Value.Text.Value.Should().Be("Hola mundo");
        result.Value.DetectedLanguage.Should().Be(LanguageCode.Create("en").Value);
    }

    [Fact]
    public async Task TranslateAsync_ShouldSurfaceSpanishAsTheDetectedLanguageSoTheDomainCanSkipIt()
    {
        // El adaptador no aplica la regla de negocio: se limita a informar del idioma
        // detectado y es el agregado quien decide. Separar eso es lo que mantiene la
        // regla testeable sin red.
        const string spanishBody = """
            [{
              "detectedLanguage": { "language": "es", "score": 1.0 },
              "translations": [{ "text": "Hello world", "to": "es" }]
            }]
            """;
        var (provider, _) = CreateSut(body: spanishBody);

        var result = await TranslateAsync(provider, "Hola mundo");

        result.IsSuccess.Should().BeTrue();
        result.Value.DetectedLanguage.Should().Be(LanguageCode.Spanish);
    }

    [Theory]
    [InlineData("[]", "ningún resultado")]
    [InlineData("""[{ "translations": [{ "text": "Hola", "to": "es" }] }]""", "idioma detectado")]
    [InlineData("""[{ "detectedLanguage": { "language": "en", "score": 1.0 } }]""", "texto traducido")]
    public async Task TranslateAsync_ShouldFailGracefullyOnAMalformedResponse(string body, string expectedHint)
    {
        var (provider, _) = CreateSut(body: body);

        var result = await TranslateAsync(provider);

        result.IsFailure.Should().BeTrue();
        result.Error.Description.Should().Contain(expectedHint);
    }

    // ---------- Errores remotos ----------

    [Fact]
    public async Task TranslateAsync_ShouldPropagateTheAzureErrorMessage()
    {
        const string errorBody = """
            { "error": { "code": 401000, "message": "The request is not authorized because credentials are missing or invalid." } }
            """;
        var (provider, _) = CreateSut(HttpStatusCode.Unauthorized, errorBody);

        var result = await TranslateAsync(provider);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("TranslationProvider.Unavailable");
        result.Error.Description.Should().Contain("401").And.Contain("credentials are missing");
    }

    [Fact]
    public async Task TranslateAsync_ShouldNotThrowWhenTheErrorBodyIsNotJson()
    {
        var (provider, _) = CreateSut(HttpStatusCode.BadGateway, "<html>502 Bad Gateway</html>", "text/html");

        var result = await TranslateAsync(provider);

        result.IsFailure.Should().BeTrue();
        result.Error.Description.Should().Contain("502");
    }
}
