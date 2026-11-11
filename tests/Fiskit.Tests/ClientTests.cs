using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fiskit.Tests
{
    public class ClientTests
    {
        private const string InvoiceJson = "{\"id\":\"inv_1\",\"object\":\"invoice\",\"status\":\"authorized\"," +
            "\"voucher_type\":\"invoice_c\",\"point_of_sale\":1,\"number\":42,\"cae\":\"76451234567890\"," +
            "\"cae_due_date\":\"2026-11-20\",\"authority_observations\":[],\"authority_errors\":[]}";

        /// <summary>Answers from a list and records every request.</summary>
        private sealed class FakeHandler : HttpMessageHandler
        {
            public readonly Queue<Func<HttpResponseMessage>> Answers = new Queue<Func<HttpResponseMessage>>();
            public readonly List<(HttpRequestMessage Request, string? Body)> Requests = new List<(HttpRequestMessage, string?)>();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                var body = request.Content == null ? null : await request.Content.ReadAsStringAsync();
                Requests.Add((request, body));
                return Answers.Dequeue()();
            }
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
            new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        private static (FiskitClient Client, FakeHandler Handler) Client()
        {
            var handler = new FakeHandler();
            var client = new FiskitClient("sk_test_x", new FiskitOptions { BaseUrl = "https://api.test" }, new HttpClient(handler));
            return (client, handler);
        }

        [Fact]
        public async Task CreatesAnInvoiceWithTheKeyTheBodyAndAnIdempotencyKey()
        {
            var (fiskit, handler) = Client();
            handler.Answers.Enqueue(() => Json(HttpStatusCode.OK, InvoiceJson));

            var invoice = await fiskit.Invoices.CreateAsync(new { voucher_type = "invoice_c", amounts = new { total = 1500 } }, "sale-1");

            Assert.True(invoice.Authorized);
            Assert.Equal(42L, invoice.Number);
            Assert.Equal("76451234567890", invoice.Cae);
            var (request, body) = handler.Requests[0];
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://api.test/v1/invoices", request.RequestUri!.ToString());
            Assert.Equal("Bearer sk_test_x", request.Headers.Authorization!.ToString());
            Assert.Equal("sale-1", request.Headers.GetValues("Idempotency-Key").Single());
            Assert.Contains("\"total\":1500", body);
        }

        [Fact]
        public async Task RetriesAnOutageWithTheSameGeneratedIdempotencyKey()
        {
            var (fiskit, handler) = Client();
            handler.Answers.Enqueue(() =>
            {
                var response = Json(HttpStatusCode.ServiceUnavailable, "{\"error\":{\"code\":\"authority_unavailable\"}}");
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(0.01));
                return response;
            });
            handler.Answers.Enqueue(() => throw new HttpRequestException("reset"));
            handler.Answers.Enqueue(() => Json(HttpStatusCode.OK, InvoiceJson));

            await fiskit.Invoices.CreateAsync(new { voucher_type = "invoice_c", amounts = new { total = 1 } });

            Assert.Equal(3, handler.Requests.Count);
            var keys = handler.Requests.Select(r => r.Request.Headers.GetValues("Idempotency-Key").Single()).Distinct().ToList();
            Assert.Single(keys);
            Assert.True(Guid.TryParse(keys[0], out _));
        }

        [Fact]
        public async Task ApiErrorsBecomeAFiskitException()
        {
            var (fiskit, handler) = Client();
            handler.Answers.Enqueue(() => Json(HttpStatusCode.BadRequest,
                "{\"error\":{\"type\":\"invalid_request_error\",\"code\":\"parameter_invalid\",\"message\":\"Bad total\"," +
                "\"param\":\"amounts.total\",\"request_id\":\"req_1\"}}"));

            var error = await Assert.ThrowsAsync<FiskitException>(() => fiskit.Invoices.RetrieveAsync("inv_1"));

            Assert.Equal(400, error.Status);
            Assert.Equal("parameter_invalid", error.Code);
            Assert.Equal("amounts.total", error.Param);
            Assert.Equal("req_1", error.RequestId);
            Assert.Equal("Bad total", error.Message);
        }

        [Fact]
        public async Task ThePdfComesBackAsBytesAndTheTaxpayerByCuit()
        {
            var (fiskit, handler) = Client();
            handler.Answers.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7")) });
            handler.Answers.Enqueue(() => Json(HttpStatusCode.OK,
                "{\"object\":\"taxpayer\",\"cuit\":30712345671,\"name\":\"ACME SA\",\"type\":\"company\",\"active\":true," +
                "\"vat_condition\":\"registered\",\"receiver_vat_condition_id\":1}"));

            Assert.Equal("%PDF-1.7", Encoding.ASCII.GetString(await fiskit.Invoices.PdfAsync("inv_1")));
            var taxpayer = await fiskit.Taxpayers.RetrieveAsync(30712345671);

            Assert.Equal("registered", taxpayer.VatCondition);
            Assert.Equal(1, taxpayer.ReceiverVatConditionId);
            Assert.Equal("application/pdf", handler.Requests[0].Request.Headers.Accept.Single().MediaType);
            Assert.EndsWith("/v1/taxpayers/30712345671", handler.Requests[1].Request.RequestUri!.ToString());
        }

        [Fact]
        public async Task APostWithoutIdempotencyKeyIsNeverRetried()
        {
            var (fiskit, handler) = Client();
            handler.Answers.Enqueue(() => throw new HttpRequestException("reset"));

            var error = await Assert.ThrowsAsync<FiskitException>(() => fiskit.Invoices.EmailAsync("inv_1", "a@b.com"));

            Assert.Equal("network", error.Code);
            Assert.Single(handler.Requests);
        }

        [Fact]
        public void VerifiesWebhooksWithTheStandardWebhooksExample()
        {
            // Example from the Standard Webhooks specification
            const string secret = "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw";
            var headers = new Dictionary<string, string>
            {
                ["Webhook-Id"] = "msg_p5jXN8AQM9LWM0D4loKWxJek",
                ["Webhook-Timestamp"] = "1614265330",
                ["Webhook-Signature"] = "v1,g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OE=",
            };
            var sent = DateTimeOffset.FromUnixTimeSeconds(1614265330);

            Assert.Equal(2432232314, Webhook.Verify("{\"test\": 2432232314}", headers, secret, now: sent).GetProperty("test").GetInt64());
            Assert.Throws<ArgumentException>(() => Webhook.Verify("{\"test\": 1}", headers, secret, now: sent));
            Assert.Throws<ArgumentException>(() => Webhook.Verify("{\"test\": 2432232314}", headers, secret, now: sent.AddHours(1)));
        }

        [Fact]
        public void AddFiskitRegistersASingletonClient()
        {
            var provider = new ServiceCollection().AddFiskit(o => o.ApiKey = "sk_test_x").BuildServiceProvider();

            Assert.Same(provider.GetRequiredService<FiskitClient>(), provider.GetRequiredService<FiskitClient>());
        }
    }
}
