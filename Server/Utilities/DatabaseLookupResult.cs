namespace Server.Utilities
{
    public class DatabaseLookupResult
    {
        public string Db { get; set; } = string.Empty;
        public long Db_Bytes { get; set; }
        public string? Wal { get; set; }
        public long? Wal_Bytes { get; set; }
        public string? Shm { get; set; }
        public long? Shm_Bytes { get;set; }
    }
}
