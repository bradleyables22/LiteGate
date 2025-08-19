
using Server.Database.Models;
using System.Data.SQLite;
using System.Text.Json.Serialization;

namespace Server.Authentication.Models
{
    public class SubscriptionRecord
    {

        public string Id { get; set; } = Guid.CreateVersion7().ToString();
        public string UserId { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Database { get; set; } = string.Empty;
        public string Table { get; set; } = string.Empty;
        [JsonIgnore]
        public string Secret { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public UpdateEventType Event { get; set; }
    }
}
