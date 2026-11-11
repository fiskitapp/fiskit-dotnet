# FisKit for .NET

[Español más abajo](#español)

Official client of the [FisKit](https://fiskit.com.ar) API: Argentine electronic invoices with ARCA (formerly AFIP) in a single request. For .NET 8 and .NET Framework 4.6.1+ (netstandard2.0).

```bash
dotnet add package Fiskit
```

```csharp
using Fiskit;

var fiskit = new FiskitClient(Environment.GetEnvironmentVariable("FISKIT_API_KEY"));

// Your sale id as the key: a retry never issues a second invoice
var invoice = await fiskit.Invoices.CreateAsync(
    new { voucher_type = "invoice_c", amounts = new { total = 1500 } },
    idempotencyKey: "sale-8812");

Console.WriteLine($"{invoice.Number} {invoice.Cae}");

byte[] pdf = await fiskit.Invoices.PdfAsync(invoice.Id!);          // PDF with ARCA's QR code
await fiskit.Invoices.EmailAsync(invoice.Id!, "customer@example.com");
var taxpayer = await fiskit.Taxpayers.RetrieveAsync(30712345671);  // name and VAT condition of a CUIT
```

**ASP.NET Core:** `builder.Services.AddFiskit(o => o.ApiKey = builder.Configuration["Fiskit:ApiKey"]);` and inject `FiskitClient`.

**Webhooks:** `Webhook.Verify(rawBody, headers, secret)` checks the Standard Webhooks signature and returns the event.

`sk_test_...` keys issue against ARCA's homologation environment (no fiscal value); `sk_live_...` keys issue real invoices. Outages are retried with the same idempotency key. Errors are `FiskitException` with `Status`, `Type`, `Code`, `Param` and `RequestId`.

Full documentation: https://fiskit.com.ar/developers

Coding with an AI agent? Point it to https://fiskit.com.ar/llms.txt: the API rules, errors and examples in plain text.

---

## Español

Cliente oficial de la API de [FisKit](https://fiskit.com.ar): facturas electrónicas de ARCA (ex AFIP) con un solo pedido. Para .NET 8 y .NET Framework 4.6.1 o superior.

```csharp
var fiskit = new FiskitClient(Environment.GetEnvironmentVariable("FISKIT_API_KEY"));

// Con el id de tu venta como clave, un reintento nunca duplica la factura
var factura = await fiskit.Invoices.CreateAsync(
    new { voucher_type = "invoice_c", amounts = new { total = 1500 } },
    idempotencyKey: "venta-8812");
```

En ASP.NET Core: `builder.Services.AddFiskit(o => o.ApiKey = builder.Configuration["Fiskit:ApiKey"]);` y pedís `FiskitClient` por inyección. Las claves `sk_test_...` emiten en homologación, sin validez fiscal; las `sk_live_...` emiten facturas reales.

Documentación completa: https://fiskit.com.ar/developers

¿Programás con un agente de IA? Pasale https://fiskit.com.ar/llms.txt: las reglas, errores y ejemplos de la API en texto plano.
