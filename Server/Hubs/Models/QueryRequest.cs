using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Server.Hubs.Models
{
    public class QueryRequest 
    {
        [ReadOnly(true)]
        [JsonIgnore]
        public Guid RequestId { get; set; } = Guid.CreateVersion7();
        [Required]
        public string Database { get; set; } = string.Empty;
        [Required]
        public string Query { get; set; } = string.Empty;
        public Dictionary<string, object?>? Params { get; set; }
        public int? Timeout { get; set; } = 30;
    }
}
