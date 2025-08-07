using Microsoft.Extensions.Diagnostics.HealthChecks;
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
    }
}
