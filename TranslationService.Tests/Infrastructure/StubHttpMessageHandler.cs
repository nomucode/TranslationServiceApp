using System.Net;
using System.Text;

namespace TranslationService.Tests.Infrastructure;

/// Doble de la capa de transporte: captura la petición saliente para poder afirmar sobre
/// URL y cabeceras, y devuelve una respuesta guionizada. Es lo que permite probar el
/// adaptador de Azure sin red y de forma determinista.
internal sealed class StubHttpMessageHandler(
    HttpStatusCode statusCode,
    string body,
    string contentType = "application/json") : HttpMessageHandler
{
    private readonly List<HttpRequestMessage> _requests = [];

    public IReadOnlyList<HttpRequestMessage> Requests => _requests;

    public HttpRequestMessage LastRequest => _requests[^1];

    public string? LastRequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        _requests.Add(request);

        if (request.Content is not null)
        {
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, contentType),
            RequestMessage = request
        };
    }
}
