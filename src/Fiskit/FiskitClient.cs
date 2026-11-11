using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Fiskit
{
    /// <summary>
    /// Client of the FisKit API. Thread-safe: register it once (see <c>AddFiskit</c>) or create one and reuse it.
    /// </summary>
    /// <example>
    /// <code>
    /// var fiskit = new FiskitClient(Environment.GetEnvironmentVariable("FISKIT_API_KEY"));
    /// var invoice = await fiskit.Invoices.CreateAsync(
    ///     new { voucher_type = "invoice_c", amounts = new { total = 1500 } }, idempotencyKey: "sale-8812");
    /// </code>
    /// </example>
    public sealed class FiskitClient
    {
        public const string Version = "0.1.0";
        public const string DefaultBaseUrl = "https://api.fiskit.com.ar";
        private static readonly HashSet<int> Retryable = new HashSet<int> { 408, 409, 429, 500, 502, 503, 504 };

        private readonly string _apiKey;
        private readonly string _baseUrl;
        private readonly int _maxRetries;
        private readonly HttpClient _http;

        public Invoices Invoices { get; }
        public Taxpayers Taxpayers { get; }

        /// <param name="apiKey">sk_test_... or sk_live_...; by default the FISKIT_API_KEY environment variable.</param>
        /// <param name="options">Base URL, retries and timeout; defaults are fine.</param>
        /// <param name="httpClient">Optional: an HttpClient from IHttpClientFactory.</param>
        public FiskitClient(string? apiKey = null, FiskitOptions? options = null, HttpClient? httpClient = null)
        {
            options ??= new FiskitOptions();
            var key = apiKey ?? options.ApiKey ?? Environment.GetEnvironmentVariable("FISKIT_API_KEY");
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Missing API key: pass it or set FISKIT_API_KEY");
            }
            _apiKey = key!;
            _baseUrl = (options.BaseUrl ?? DefaultBaseUrl).TrimEnd('/');
            _maxRetries = options.MaxRetries;
            _http = httpClient ?? new HttpClient { Timeout = options.Timeout };
            Invoices = new Invoices(this);
            Taxpayers = new Taxpayers(this);
        }

        internal async Task<JsonElement> RequestJsonAsync(HttpMethod method, string path, object? body,
            string? idempotencyKey, CancellationToken cancellationToken)
        {
            var bytes = await RequestAsync(method, path, body, idempotencyKey, false, cancellationToken).ConfigureAwait(false);
            if (bytes.Length == 0)
            {
                return default;
            }
            using var document = JsonDocument.Parse(bytes);
            return document.RootElement.Clone();
        }

        internal async Task<byte[]> RequestAsync(HttpMethod method, string path, object? body, string? idempotencyKey,
            bool binary, CancellationToken cancellationToken)
        {
            var payload = body == null ? null : JsonSerializer.SerializeToUtf8Bytes(body);
            // Without an idempotency key a POST is never repeated: it might have been processed
            var retryAllowed = method == HttpMethod.Get || idempotencyKey != null;

            for (var attempt = 0; ; attempt++)
            {
                using var request = new HttpRequestMessage(method, _baseUrl + path);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(binary ? "application/pdf" : "application/json"));
                request.Headers.UserAgent.ParseAdd("fiskit-dotnet/" + Version);
                if (idempotencyKey != null)
                {
                    request.Headers.Add("Idempotency-Key", idempotencyKey);
                }
                if (payload != null)
                {
                    request.Content = new ByteArrayContent(payload);
                    request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                }

                HttpResponseMessage response;
                try
                {
                    response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException e)
                {
                    if (retryAllowed && attempt < _maxRetries)
                    {
                        await WaitAsync(attempt, null, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    throw new FiskitException(0, "connection_error", "network", e.Message, null, null);
                }

                using (response)
                {
                    var status = (int)response.StatusCode;
                    var content = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    if (status >= 200 && status < 300)
                    {
                        return content;
                    }
                    if (Retryable.Contains(status) && retryAllowed && attempt < _maxRetries)
                    {
                        await WaitAsync(attempt, response.Headers.RetryAfter?.Delta, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    var requestId = response.Headers.TryGetValues("Request-Id", out var values) ? values.FirstOrDefault() : null;
                    throw FiskitException.From(status, content, requestId);
                }
            }
        }

        private static Task WaitAsync(int attempt, TimeSpan? retryAfter, CancellationToken cancellationToken)
        {
            var delay = retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero
                ? TimeSpan.FromSeconds(Math.Min(retryAfter.Value.TotalSeconds, 30))
                : TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt));
            return Task.Delay(delay, cancellationToken);
        }
    }

    /// <summary>Settings of <see cref="FiskitClient"/>; bind them from configuration with <c>AddFiskit</c>.</summary>
    public sealed class FiskitOptions
    {
        public string? ApiKey { get; set; }
        public string? WebhookSecret { get; set; }
        public string? BaseUrl { get; set; }
        public int MaxRetries { get; set; } = 2;
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);
    }
}
