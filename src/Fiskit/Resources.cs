using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Fiskit
{
    /// <summary>Issue invoices and credit or debit notes, look them up, download their PDF and email them.</summary>
    public sealed class Invoices
    {
        private readonly FiskitClient _client;

        internal Invoices(FiskitClient client)
        {
            _client = client;
        }

        /// <summary>
        /// Issues an invoice. Without an idempotency key one is generated, so the automatic retries never issue a
        /// second invoice. Pass your own (for example your sale id) to make your retries safe too.
        /// </summary>
        /// <param name="body">The request as documented, for example
        /// <c>new { voucher_type = "invoice_c", amounts = new { total = 1500 } }</c>.</param>
        /// <param name="idempotencyKey">Your sale id, for example; generated if null.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        public async Task<Invoice> CreateAsync(object body, string? idempotencyKey = null,
            CancellationToken cancellationToken = default)
        {
            var json = await _client.RequestJsonAsync(HttpMethod.Post, "/v1/invoices", body,
                idempotencyKey ?? Guid.NewGuid().ToString(), cancellationToken).ConfigureAwait(false);
            return Invoice.From(json);
        }

        public async Task<Invoice> RetrieveAsync(string id, CancellationToken cancellationToken = default)
        {
            var json = await _client.RequestJsonAsync(HttpMethod.Get, "/v1/invoices/" + Uri.EscapeDataString(id), null,
                null, cancellationToken).ConfigureAwait(false);
            return Invoice.From(json);
        }

        /// <summary>The voucher PDF, with the QR code ARCA requires.</summary>
        public Task<byte[]> PdfAsync(string id, CancellationToken cancellationToken = default) =>
            _client.RequestAsync(HttpMethod.Get, "/v1/invoices/" + Uri.EscapeDataString(id) + "/pdf", null, null, true,
                cancellationToken);

        /// <summary>Emails the PDF to the customer; without an email, to receiver.email of the invoice.</summary>
        /// <returns>The address it was sent to.</returns>
        public async Task<string> EmailAsync(string id, string? email = null, CancellationToken cancellationToken = default)
        {
            object body = email == null ? new { } : (object)new { email };
            var json = await _client.RequestJsonAsync(HttpMethod.Post,
                "/v1/invoices/" + Uri.EscapeDataString(id) + "/email", body, null, cancellationToken).ConfigureAwait(false);
            return json.GetProperty("email").GetString() ?? string.Empty;
        }
    }

    /// <summary>Look up a CUIT in the ARCA registry.</summary>
    public sealed class Taxpayers
    {
        private readonly FiskitClient _client;

        internal Taxpayers(FiskitClient client)
        {
            _client = client;
        }

        /// <summary>Name, fiscal address and VAT condition of a CUIT.</summary>
        public async Task<Taxpayer> RetrieveAsync(long cuit, CancellationToken cancellationToken = default)
        {
            var json = await _client.RequestJsonAsync(HttpMethod.Get, "/v1/taxpayers/" + cuit, null, null,
                cancellationToken).ConfigureAwait(false);
            return new Taxpayer(
                json.GetProperty("cuit").GetInt64(),
                Invoice.Text(json, "name"),
                Invoice.Text(json, "type"),
                json.TryGetProperty("active", out var active) && active.GetBoolean(),
                Invoice.Text(json, "vat_condition"),
                json.TryGetProperty("receiver_vat_condition_id", out var condition) ? condition.GetInt32() : 0,
                json);
        }
    }

    /// <summary>An invoice as the API returns it. <see cref="Raw"/> has every field, including ARCA's messages.</summary>
    public sealed class Invoice
    {
        public string? Id { get; private set; }
        /// <summary>pending, processing, authorized, rejected, reconciling or failed.</summary>
        public string? Status { get; private set; }
        public string? VoucherType { get; private set; }
        public int PointOfSale { get; private set; }
        /// <summary>Null until authorized.</summary>
        public long? Number { get; private set; }
        public string? Cae { get; private set; }
        public DateTime? CaeDueDate { get; private set; }
        public JsonElement Raw { get; private set; }

        public bool Authorized => Status == "authorized";

        internal static Invoice From(JsonElement json) => new Invoice
        {
            Id = Text(json, "id"),
            Status = Text(json, "status"),
            VoucherType = Text(json, "voucher_type"),
            PointOfSale = json.TryGetProperty("point_of_sale", out var pos) ? pos.GetInt32() : 0,
            Number = json.TryGetProperty("number", out var number) && number.ValueKind == JsonValueKind.Number
                ? number.GetInt64() : (long?)null,
            Cae = Text(json, "cae"),
            CaeDueDate = Text(json, "cae_due_date") is string due ? DateTime.Parse(due) : (DateTime?)null,
            Raw = json,
        };

        internal static string? Text(JsonElement json, string field) =>
            json.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    /// <summary>
    /// A CUIT in the ARCA registry. Type is person or company; VatCondition is registered, monotributo, exempt or
    /// not_registered; ReceiverVatConditionId is the value to send as receiver.vat_condition_id when invoicing them.
    /// </summary>
    public sealed class Taxpayer
    {
        public Taxpayer(long cuit, string? name, string? type, bool active, string? vatCondition,
            int receiverVatConditionId, JsonElement raw)
        {
            Cuit = cuit;
            Name = name;
            Type = type;
            Active = active;
            VatCondition = vatCondition;
            ReceiverVatConditionId = receiverVatConditionId;
            Raw = raw;
        }

        public long Cuit { get; }
        public string? Name { get; }
        public string? Type { get; }
        public bool Active { get; }
        public string? VatCondition { get; }
        public int ReceiverVatConditionId { get; }
        public JsonElement Raw { get; }
    }
}
