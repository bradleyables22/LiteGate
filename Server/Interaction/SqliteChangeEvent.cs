

using Server.Interaction.Enums;

namespace Server.Interaction
{
    public class SqliteChangeEvent
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public UpdateEventType EventType { get; set; } = UpdateEventType.Update;
        public string Database { get; set; } = string.Empty;
        public string Table { get; set; } = string.Empty;
        public long RowId { get; init; }
    }
}
