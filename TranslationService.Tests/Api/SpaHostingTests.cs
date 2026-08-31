using System.Net;
using System.Text.RegularExpressions;

namespace TranslationService.Tests.Api;

/// Verifica la decisión de hospedar el WASM dentro de la propia Api: un solo origen, un
/// solo proceso y cero CORS. Si alguien rompiera el orden de UseBlazorFrameworkFiles /
/// UseStaticFiles / MapFallbackToFile, estos tests lo detectarían.
public sealed class SpaHostingTests : IAsyncLifetime
{
    private TranslationApiFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new TranslationApiFactory();
        _client = _factory.CreateClient();

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Root_ShouldServeTheBlazorHost()
    {
        var response = await _client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await response.Content.ReadAsStringAsync()).Should().Contain("blazor.webassembly");
    }

    [Fact]
    public async Task UnknownClientRoute_ShouldFallBackToTheSpaSoBlazorCanRouteIt()
    {
        var response = await _client.GetAsync("/alguna/ruta/del/cliente");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
    }

    [Fact]
    public async Task UnknownApiRoute_ShouldReturnProblemDetailsAndNeverTheSpa()
    {
        // El cortafuegos entre API y SPA: un cliente de API que se equivoca de ruta debe
        // recibir un 404 JSON, no una página HTML con 200.
        var response = await _client.GetAsync("/api/no-existe");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task BlazorFrameworkFiles_ShouldBeServedFromTheApi()
    {
        // El nombre del script lleva huella digital para el cacheado, así que se extrae del
        // index.html servido en lugar de codificarlo: el test valida el enlace real entre
        // el host y los artefactos del WASM, no un nombre que el build puede cambiar.
        var host = await _client.GetStringAsync("/");
        var scriptPath = Regex.Match(host, @"src=""(_framework/blazor\.webassembly[^""]*\.js)""").Groups[1].Value;

        scriptPath.Should().NotBeEmpty("el index.html debe referenciar el arranque de Blazor");

        var response = await _client.GetAsync($"/{scriptPath}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
