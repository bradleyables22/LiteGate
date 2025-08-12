using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Globalization;
using System.Net.Mime;
using System.Text.Json;

namespace Server.Extensions
{
    public static class ApiExtensions
    {
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
                        if (je.TryGetDateTimeOffset(out var dto)) 
                            return dto;
                        return je.GetString();

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
                    return g.ToString("D");

                case DateTime dt:
                    return DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);

                case DateTimeOffset dto:
                    return dto.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

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
                isoUtc = dt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
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
                isoUtc = dt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
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
