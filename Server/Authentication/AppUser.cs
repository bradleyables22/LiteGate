using System.Text.Json;

namespace Server.Authentication
{
    public class AppUser
    {
        public string Id { get; set; } = Guid.CreateVersion7().ToString();

        public string UserName { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? DisabledAt { get; set; }
        public List<DatabaseRole> Roles { get; set; } = new();

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
        public string Database { get; set; } = string.Empty;
        public SystemRole Role { get; set; } = SystemRole.viewer;
    }

    public enum SystemRole
    {
        admin,owner,editor,viewer
    }
}

