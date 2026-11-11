using System;
using System.Text.Json;

namespace Fiskit
{
    /// <summary>An error answered by the API, or a network failure (status 0).</summary>
    public sealed class FiskitException : Exception
    {
        public FiskitException(int status, string type, string code, string message, string? param, string? requestId)
            : base(message)
        {
            Status = status;
            Type = type;
            Code = code;
            Param = param;
            RequestId = requestId;
        }

        /// <summary>HTTP status, or 0 if the API could not be reached.</summary>
        public int Status { get; }
        /// <summary>For example invalid_request_error, authority_error or api_error.</summary>
        public string Type { get; }
        /// <summary>For example parameter_invalid or invoice_rejected.</summary>
        public string Code { get; }
        /// <summary>The field with the problem, if any.</summary>
        public string? Param { get; }
        /// <summary>Give it to support to find the request.</summary>
        public string? RequestId { get; }

        internal static FiskitException From(int status, byte[] body, string? headerRequestId)
        {
            string type = "api_error", code = "http_" + status, message = "HTTP " + status;
            string? param = null, requestId = headerRequestId;
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty("error", out var error))
                {
                    type = Invoice.Text(error, "type") ?? type;
                    code = Invoice.Text(error, "code") ?? code;
                    message = Invoice.Text(error, "message") ?? message;
                    param = Invoice.Text(error, "param");
                    requestId = Invoice.Text(error, "request_id") ?? requestId;
                }
            }
            catch (JsonException)
            {
                // Not JSON: keep the HTTP status as the message
            }
            return new FiskitException(status, type, code, message, param, requestId);
        }
    }
}
