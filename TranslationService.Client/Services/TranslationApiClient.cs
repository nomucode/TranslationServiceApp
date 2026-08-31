using System.Net.Http.Json;
using System.Text.Json;
using TranslationService.Contracts.Translations;

namespace TranslationService.Client.Services;

public interface ITranslationApiClient
{
    Task<ApiResult<TranslationAcceptedResponse>> CreateAsync(string text, CancellationToken cancellationToken);

    Task<ApiResult<TranslationJobResponse>> GetAsync(string statusUrl, CancellationToken cancellationToken);
}

/// Traduce la respuesta HTTP a ApiResult. Su responsabilidad clave es que ningún fallo de
/// red o de servidor escape como excepción hacia el componente: la UI de un chat nunca debe
/// romperse porque una petición falle.
public sealed class TranslationApiClient(HttpClient httpClient) : ITranslationApiClient
{
    private const string Endpoint = "api/translations";

    public async Task<ApiResult<TranslationAcceptedResponse>> CreateAsync(
        string text,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                Endpoint,
                new CreateTranslationRequest(text),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return ApiResult<TranslationAcceptedResponse>.Fail(
                    await ReadProblemDetailAsync(response, cancellationToken));
            }

            var accepted = await response.Content.ReadFromJsonAsync<TranslationAcceptedResponse>(cancellationToken);

            return accepted is null
                ? ApiResult<TranslationAcceptedResponse>.Fail("El servidor aceptó la petición pero no devolvió el identificador.")
                : ApiResult<TranslationAcceptedResponse>.Ok(accepted);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ApiResult<TranslationAcceptedResponse>.Fail("No se pudo contactar con el servidor.");
        }
    }

    public async Task<ApiResult<TranslationJobResponse>> GetAsync(
        string statusUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            // statusUrl llega del propio 202 (cabecera Location), así que el cliente no
            // compone rutas: si el servidor las cambia, el frontend no se entera.
            var job = await httpClient.GetFromJsonAsync<TranslationJobResponse>(
                statusUrl.TrimStart('/'),
                cancellationToken);

            return job is null
                ? ApiResult<TranslationJobResponse>.Fail("El servidor no devolvió el estado del trabajo.")
                : ApiResult<TranslationJobResponse>.Ok(job);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ApiResult<TranslationJobResponse>.Fail("No se pudo consultar el estado del trabajo.");
        }
    }

    /// La API responde ProblemDetails (RFC 7807); se extrae 'detail' para poder mostrar al
    /// usuario el motivo real —"el texto no puede estar vacío"— en lugar de un genérico.
    private static async Task<string> ReadProblemDetailAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));

            return document.RootElement.TryGetProperty("detail", out var detail)
                   && detail.GetString() is { Length: > 0 } message
                ? message
                : $"El servidor respondió {(int)response.StatusCode}.";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return $"El servidor respondió {(int)response.StatusCode}.";
        }
    }
}
