using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Server.Utiilites
{
    public class SqlRequest
    {
        [ReadOnly(true)]
        [JsonIgnore]
        public Guid RequestId { get; set; } = Guid.CreateVersion7();
        [Required]
        public string Database { get; set; } = string.Empty;
        [Required]
        public string Statement { get; set; } = string.Empty;
        public int? Timeout { get; set; } = 30;
    }
}
