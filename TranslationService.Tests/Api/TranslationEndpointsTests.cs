using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TranslationService.Contracts.Translations;

namespace TranslationService.Tests.Api;

public sealed class TranslationEndpointsTests : IAsyncLifetime
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

    /// Sondea el GET igual que hará el frontend, hasta que el trabajo alcance un estado
    /// terminal. Es la prueba de que el patrón funciona de extremo a extremo sobre HTTP.
    private async Task<TranslationJobResponse> PollUntilTerminalAsync(string statusUrl)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var response = await _client.GetAsync(statusUrl);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var job = (await response.Content.ReadFromJsonAsync<TranslationJobResponse>())!;
            if (job.IsTerminal)
            {
                return job;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"El trabajo de {statusUrl} no terminó en 10 s.");
    }

    private async Task<TranslationAcceptedResponse> PostAsync(string text)
    {
        var response = await _client.PostAsJsonAsync("/api/translations", new CreateTranslationRequest(text));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        return (await response.Content.ReadFromJsonAsync<TranslationAcceptedResponse>())!;
    }

    // ---------- POST: el contrato del 202 ----------

    [Fact]
    public async Task Post_ShouldReturn202WithALocationHeaderPointingAtTheStatusResource()
    {
        var response = await _client.PostAsJsonAsync("/api/translations", new CreateTranslationRequest("Hello world"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var accepted = (await response.Content.ReadFromJsonAsync<TranslationAcceptedResponse>())!;
        accepted.JobId.Should().NotBe(Guid.Empty);
        accepted.Status.Should().Be(TranslationJobStatus.Pending);
        accepted.StatusUrl.Should().Be($"/api/translations/{accepted.JobId}");
        response.Headers.Location!.ToString().Should().EndWith(accepted.StatusUrl);
    }

    [Fact]
    public async Task Post_ShouldReturnImmediatelyEvenWhenTranslationIsSlow()
    {
        // El núcleo del patrón: la latencia del proveedor no debe aparecer en la respuesta.
        _factory.Translator.Latency = TimeSpan.FromSeconds(2);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var response = await _client.PostAsJsonAsync("/api/translations", new CreateTranslationRequest("Slow one"));
        stopwatch.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }

    // ---------- Flujo completo ----------

    [Fact]
    public async Task FullFlow_ShouldReachCompletedWithTheTranslation()
    {
        _factory.Translator.Returns("Hello world", "Hola mundo", "en");

        var accepted = await PostAsync("Hello world");
        var job = await PollUntilTerminalAsync(accepted.StatusUrl);

        job.Status.Should().Be(TranslationJobStatus.Completed);
        job.SourceText.Should().Be("Hello world");
        job.TranslatedText.Should().Be("Hola mundo");
        job.DetectedLanguage.Should().Be("en");
        job.WasTranslated.Should().BeTrue();
        job.TargetLanguage.Should().Be("es");
    }

    [Fact]
    public async Task FullFlow_ShouldSkipTranslationForTextAlreadyInSpanish()
    {
        // La regla de negocio, ahora observada desde fuera a través de HTTP.
        const string spanish = "Hola mundo";
        _factory.Translator.Returns(spanish, "Hello world", "es");

        var accepted = await PostAsync(spanish);
        var job = await PollUntilTerminalAsync(accepted.StatusUrl);

        job.Status.Should().Be(TranslationJobStatus.Completed);
        job.WasTranslated.Should().BeFalse();
        job.TranslatedText.Should().Be(spanish);
    }

    [Fact]
    public async Task FullFlow_ShouldReachFailedWithAReadableReasonWhenTheProviderIsDown()
    {
        _factory.Translator.Fails("Broken", "Azure devolvió 503 (ServiceUnavailable)");

        var accepted = await PostAsync("Broken");
        var job = await PollUntilTerminalAsync(accepted.StatusUrl);

        job.Status.Should().Be(TranslationJobStatus.Failed);
        job.FailureReason.Should().Contain("503");
        job.TranslatedText.Should().BeEmpty();
    }

    [Fact]
    public async Task FullFlow_ShouldHandleManyConcurrentRequests()
    {
        var texts = Enumerable.Range(0, 20).Select(i => $"Message number {i}").ToList();

        var accepted = await Task.WhenAll(texts.Select(PostAsync));
        var jobs = await Task.WhenAll(accepted.Select(a => PollUntilTerminalAsync(a.StatusUrl)));

        jobs.Should().OnlyContain(job => job.Status == TranslationJobStatus.Completed);
        jobs.Select(job => job.JobId).Should().OnlyHaveUniqueItems();
    }

    // ---------- Errores como ProblemDetails ----------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Post_ShouldReturn400ProblemDetailsForBlankText(string text)
    {
        var response = await _client.PostAsJsonAsync("/api/translations", new CreateTranslationRequest(text));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("status").GetInt32().Should().Be(400);
        problem.RootElement.GetProperty("code").GetString().Should().Be("SourceText.Empty");
        problem.RootElement.TryGetProperty("traceId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Post_ShouldReturn400WhenTheTextExceedsTheAzureLimit()
    {
        var tooLong = new string('a', 5_001);

        var response = await _client.PostAsJsonAsync("/api/translations", new CreateTranslationRequest(tooLong));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().Should().Be("SourceText.TooLong");
    }

    [Fact]
    public async Task Post_ShouldReturn400ForAMalformedBody()
    {
        var response = await _client.PostAsync(
            "/api/translations",
            new StringContent("{ esto no es json", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Get_ShouldReturn404ProblemDetailsForAnUnknownJob()
    {
        var response = await _client.GetAsync($"/api/translations/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().Should().Be("TranslationJob.NotFound");
    }

    [Fact]
    public async Task Get_ShouldNotMatchTheRouteForAnIdentifierThatIsNotAGuid()
    {
        // La restricción :guid de la ruta descarta la petición antes de llegar al handler.
        var response = await _client.GetAsync("/api/translations/no-soy-un-guid");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
