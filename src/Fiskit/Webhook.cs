using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Fiskit
{
    /// <summary>Checks the webhooks FisKit sends (Standard Webhooks signatures).</summary>
    public static class Webhook
    {
        /// <summary>Checks the signature and returns the parsed event. Pass the raw body as received.</summary>
        /// <exception cref="ArgumentException">The signature is wrong or the message is too old.</exception>
        public static JsonElement Verify(string rawBody, IEnumerable<KeyValuePair<string, string>> headers, string secret,
            int toleranceSeconds = 300, DateTimeOffset? now = null)
        {
            var lowered = headers.ToDictionary(h => h.Key.ToLowerInvariant(), h => h.Value);
            if (!lowered.TryGetValue("webhook-id", out var id) || !lowered.TryGetValue("webhook-timestamp", out var timestamp)
                || !lowered.TryGetValue("webhook-signature", out var signatures))
            {
                throw new ArgumentException("Missing webhook headers");
            }
            var current = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
            if (Math.Abs(current - long.Parse(timestamp)) > toleranceSeconds)
            {
                throw new ArgumentException("Webhook timestamp out of tolerance");
            }
            var key = Convert.FromBase64String(secret.StartsWith("whsec_") ? secret.Substring(6) : secret);
            byte[] expected;
            using (var hmac = new HMACSHA256(key))
            {
                expected = Encoding.ASCII.GetBytes(Convert.ToBase64String(
                    hmac.ComputeHash(Encoding.UTF8.GetBytes(id + "." + timestamp + "." + rawBody))));
            }
            foreach (var candidate in signatures.Split(' '))
            {
                var received = Encoding.ASCII.GetBytes(candidate.StartsWith("v1,") ? candidate.Substring(3) : candidate);
                if (FixedTimeEquals(expected, received))
                {
                    using var document = JsonDocument.Parse(rawBody);
                    return document.RootElement.Clone();
                }
            }
            throw new ArgumentException("Invalid webhook signature");
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }
            var difference = 0;
            for (var i = 0; i < a.Length; i++)
            {
                difference |= a[i] ^ b[i];
            }
            return difference == 0;
        }
    }
}
