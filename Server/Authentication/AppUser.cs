using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Server.Authentication
{
    public class AppUser
    {
        public string Id { get; set; } = Guid.CreateVersion7().ToString();

        public string UserName { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonIgnore]
        public string PasswordHash { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? DisabledAt { get; set; }
        public List<DatabaseRole> Roles { get; set; } = new();
        [System.Text.Json.Serialization.JsonIgnore]
        public string RolesJson
        {
            get => JsonSerializer.Serialize(Roles);
            set => Roles = string.IsNullOrWhiteSpace(value)
                ? new List<DatabaseRole>()
                : JsonSerializer.Deserialize<List<DatabaseRole>>(value) ?? new();
        }
    }

    public class DatabaseRole
    {
        [Required(ErrorMessage = "Database name is required.")]
        [StringLength(255, MinimumLength = 4, ErrorMessage = "Database name must not exceed 64 characters.")]
        [RegularExpression(@"^[a-zA-Z][a-zA-Z0-9_.-]*\.db$", ErrorMessage = "Database name must start with a letter, use only valid characters, and end with '.db'.")]
        public string Database { get; set; } = string.Empty;

        [Required(ErrorMessage = "Role is required.")]
        [EnumDataType(typeof(SystemRole), ErrorMessage = "Invalid system role.")]
        public SystemRole Role { get; set; } = SystemRole.viewer;
    }

    public enum SystemRole
    {
        admin,owner,editor,viewer
    }
}

