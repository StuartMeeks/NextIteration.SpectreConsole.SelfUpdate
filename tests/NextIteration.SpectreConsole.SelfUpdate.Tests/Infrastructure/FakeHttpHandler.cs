using System.Net;

namespace NextIteration.SpectreConsole.SelfUpdate.Tests.Infrastructure
{
    /// <summary>
    /// Test handler that responds to each <see cref="HttpClient"/> request
    /// with a caller-supplied function. Records every request so tests can
    /// assert on URLs, headers, and call counts.
    /// </summary>
    internal sealed class FakeHttpHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage>? Responder { get; set; }
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var responder = Responder ?? throw new InvalidOperationException("FakeHttpHandler.Responder is not configured.");
            return Task.FromResult(responder(request));
        }

        public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            };

        public static HttpResponseMessage Bytes(byte[] payload, HttpStatusCode status = HttpStatusCode.OK)
        {
            var content = new ByteArrayContent(payload);
            content.Headers.ContentLength = payload.LongLength;
            return new HttpResponseMessage(status) { Content = content };
        }

        public static HttpResponseMessage NotFound() => new(HttpStatusCode.NotFound);
    }

    internal sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
