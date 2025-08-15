using System.Data.SQLite;

namespace Server.Database.Models
{
    public class SqliteChangeEvent
    {
        public UpdateEventType EventType { get; set; } = UpdateEventType.Update;
        public string Database { get; set; } = string.Empty;
        public string Table { get; set; } = string.Empty;
        public long RowId { get; init; }
    }
}
