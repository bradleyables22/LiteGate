using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Server.Authentication.Models;
using Server.Interaction;
using Server.Services;
using System.Globalization;
using System.Net.Mime;
using System.Text;
using System.Text.Json;

namespace Server.Extensions
{
    public static class ApiExtensions
    {

        public static string BuildPayload(this SqliteChangeEvent ev, SubscriptionRecord subscription)
        {
            var payload = new
            {
                SubscriptionId = subscription.Id,
                subscription.UserId,
                ev.Timestamp,
                Scope = new { database = ev.Database, table = ev.Table, Event=ev.EventType, EventString = ev.EventType.ToString() },
                ev.RowId,
            };
            return JsonSerializer.Serialize(payload);
        }

        public static async Task DeliverWithRetriesAsync(this HttpClient http, SubscriptionRecord sub, SqliteChangeEvent changeEvent, CancellationToken ct)
        {
            var delays = new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5) };
            var jsonBody = changeEvent.BuildPayload(sub);
            for (int attempt = 0; ; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                using var req = new HttpRequestMessage(HttpMethod.Post, sub.Url)
                {
                    Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
                };

                req.Headers.Add("X-Webhook-Id", sub.Id);
                req.Headers.Add("X-Webhook-Timestamp", ts);

                if (!string.IsNullOrWhiteSpace(sub.Secret))
                {
                    var sig = ChangeEventSigner.ComputeSignature(sub.Secret!, ts, jsonBody);
                    req.Headers.Add("X-Webhook-Signature", $"t={ts},v1={sig}");
                }

                req.Headers.Add("X-Idempotency-Key", $"{sub.Id}:{ts}");

                if (attempt > 0) 
                    req.Headers.Add("X-Webhook-Retry", attempt.ToString());

                HttpResponseMessage? resp = null;
                try
                {
                    resp = await http.SendAsync(req, ct);

                    if ((int)resp.StatusCode is >= 200 and < 300) 
                        return;
                }
                catch (TaskCanceledException) when (!ct.IsCancellationRequested)
                {
                }
                catch
                {
                }
                finally
                {
                    resp?.Dispose();
                }

                if (attempt >= delays.Length)
                    return;

                await Task.Delay(delays[attempt], ct);
            }
        }


        public static Task WriteResponse(HttpContext context, HealthReport result)
        {
            context.Response.ContentType = MediaTypeNames.Application.Json;

            var groupedChecks = result.Entries
                .SelectMany(entry => entry.Value.Tags.Select(tag => new { tag, entry }))
                .GroupBy(x => x.tag)
                .Select(group => new
                {
                    tag = group.Key,
                    checks = group.Select(x => new
                    {
                        name = x.entry.Key,
                        status = x.entry.Value.Status.ToString()
                    })
                });

            var json = JsonSerializer.Serialize(new
            {
                status = result.Status.ToString(),
                checks = groupedChecks,
                duration = result.TotalDuration
            });

            return context.Response.WriteAsync(json);
        }

        public static object? Normalize(this object? v)
        {
            if (v is null) 
                return null;

            if (v is System.Text.Json.JsonElement je)
            {
                switch (je.ValueKind)
                {
                    case System.Text.Json.JsonValueKind.Null: 
                        return null;
                    case System.Text.Json.JsonValueKind.True: 
                        return true;
                    case System.Text.Json.JsonValueKind.False: 
                        return false;

                    case System.Text.Json.JsonValueKind.Number:
                        if (je.TryGetInt64(out var l))
                            return l;
                        if (je.TryGetDouble(out var d)) 
                            return d;
                        return double.Parse(je.GetRawText());

                    case System.Text.Json.JsonValueKind.String:
                        var s = je.GetString();
                        if (string.IsNullOrEmpty(s))
                            return s;

                        bool hasOffset = System.Text.RegularExpressions.Regex.IsMatch(s, @"[+\-]\d{2}:\d{2}$");

                        if (hasOffset && je.TryGetDateTimeOffset(out var dto))
                            return dto.ToUniversalTime();

                        if (!hasOffset && DateTime.TryParse(s, out var dt))
                            return dt.ToUniversalTime();

                        return s;

                    case System.Text.Json.JsonValueKind.Array:
                    case System.Text.Json.JsonValueKind.Object:
                        return je.GetRawText();

                    default:
                        return je.GetRawText();
                }
            }

            return v;
        }

        public static string EnsureUniqueKey(string key, IDictionary<string, object?> dict)
        {
            if (!dict.ContainsKey(key))
                return key;

            var suffix = 2;
            var candidate = $"{key}_{suffix}";
            while (dict.ContainsKey(candidate))
            {
                suffix++;
                candidate = $"{key}_{suffix}";
            }
            return candidate;
        }

        public static object? NormalizeForJson(object? value)
        {
            if (value is null || value is DBNull) return null;

            switch (value)
            {
                case bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                    return value;

                case Guid g:
                    return g.ToString();

                case DateTime dt:
                    return dt.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);

                case DateTimeOffset dto:
                    return dto.ToUniversalTime().ToString();

                case byte[] bytes:
                    return $"base64:{Convert.ToBase64String(bytes)}";

                case string s:
                    if (TryParseDateTimeToIso(s, out var iso))
                        return iso;

                    //if (LooksLikeJson(s) && TryParseJsonElement(s, out var jsonElement))
                    //    return jsonElement;

                    return s;

                default:
                    return value.ToString();
            }
        }

        static bool TryParseDateTimeToIso(string s, out string isoUtc)
        {
            var styles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;

            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, styles, out var dt))
            {
                isoUtc = dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);
                return true;
            }

            string[] formats =
            {
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-dd HH:mm:ss.FFFFFFF",
                "yyyy-MM-ddTHH:mm:ss",
                "yyyy-MM-ddTHH:mm:ss.FFFFFFF",
                "yyyy-MM-dd HH:mm:ssK",
                "yyyy-MM-dd HH:mm:ss.FFFFFFFK",
                "yyyy-MM-ddTHH:mm:ssK",
                "yyyy-MM-ddTHH:mm:ss.FFFFFFFK"
            };

            if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture, styles, out dt))
            {
                isoUtc = dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);
                return true;
            }

            isoUtc = default!;
            return false;
        }

        static bool LooksLikeJson(string s)
        {
            s = s.Trim();
            if (s.Length < 2) return false;
            char first = s[0], last = s[^1];
            return (first == '{' && last == '}') || (first == '[' && last == ']');
        }

        static bool TryParseJsonElement(string s, out JsonElement element)
        {
            try
            {
                using var doc = JsonDocument.Parse(s);
                element = doc.RootElement.Clone();
                return true;
            }
            catch
            {
                element = default;
                return false;
            }
        }
    }
}
