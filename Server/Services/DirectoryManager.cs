using Server.Utilities;
using System.IO.Compression;
using System.Text.RegularExpressions;

public static class DirectoryManager
{
    private static readonly Regex SafeName = new(@"^[A-Za-z][A-Za-z0-9_.-]*$", RegexOptions.Compiled);

    public static string GetPersistentBasePath()
        => Environment.GetEnvironmentVariable("HOME") ?? AppContext.BaseDirectory;

    public static string GetRoot() => Path.Combine(GetPersistentBasePath(), "databases");

    public static string GetDatabaseFolder(string dbName)
    {
        ValidateName(dbName);
        return Path.Combine(GetRoot(), dbName);
    }

    public static string GetDatabaseFile(string dbName)
    {
        var folder = GetDatabaseFolder(dbName);

       return Path.Combine(folder, $"{dbName}.db");
        
    }

    public static void EnsureDatabaseFolder(string dbName)
    {
        var folder = GetDatabaseFolder(dbName);
        Directory.CreateDirectory(folder);
    }

    public static string BuildSqliteConnectionString(string dbName, bool readOnly = false)
    {
        EnsureDatabaseFolder(dbName);
        var dbPath = GetDatabaseFile(dbName);

        var baseConn = $"Data Source={dbPath};Default Timeout=5;";

        if (readOnly)
            baseConn += "Mode=ReadOnly;Cache=Shared;";
        
        return baseConn;
    }

    public static IReadOnlyList<DatabaseLookupResult> ListDatabases()
    {
        var root = GetRoot();
        if (!Directory.Exists(root)) return Array.Empty<DatabaseLookupResult>();

        var results = new List<DatabaseLookupResult>();

        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(dir);
            if (!SafeName.IsMatch(name)) continue;

            var dbFile = Path.Combine(dir, $"{name}.db");
            if (!File.Exists(dbFile)) continue;

            var walFile = dbFile + "-wal";
            var shmFile = dbFile + "-shm";

            results.Add(new DatabaseLookupResult
            {
                Db = $"{name}.db",
                Db_Bytes = GetFileSizeSafe(dbFile) ?? 0,
                Wal = File.Exists(walFile) ? $"{name}.db-wal" : null,
                Wal_Bytes = GetFileSizeSafe(walFile),
                Shm = File.Exists(shmFile) ? $"{name}.db-shm" : null,
                Shm_Bytes = GetFileSizeSafe(shmFile)
            });
        }

        return results;
    }

    private static long? GetFileSizeSafe(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            return fi.Exists ? fi.Length : null;
        }
        catch
        {
            return null;
        }
    }

    public static bool DeleteDatabase(string dbName)
    {
        var folder = GetDatabaseFolder(dbName);
        if (!Directory.Exists(folder)) return false;

        Directory.Delete(folder, recursive: true); // nukes db, wal, shm
        return true;
    }

    public static bool DatabaseFileExists(string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
            return false;

        if (!databaseName.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
            databaseName += ".db";

        var folder = GetDatabaseFolder(Path.GetFileNameWithoutExtension(databaseName));
        var path = Path.Combine(folder, databaseName);

        return File.Exists(path);
    }

    public static async Task<byte[]> GetDatabaseBytesAsync(string dbName)
    {
        dbName = dbName.Replace(".db","");

        var dbFile = GetDatabaseFile(dbName);
        if (!File.Exists(dbFile))
            throw new FileNotFoundException("Database file not found.", dbFile);

        await using var fs = new FileStream(dbFile, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        using var ms = new MemoryStream();
        await fs.CopyToAsync(ms);
        return ms.ToArray();
    }

    public static void ValidateName(string dbName)
    {
        if (string.IsNullOrWhiteSpace(dbName))
            throw new ArgumentException("Database name is required.", nameof(dbName));
        if (!SafeName.IsMatch(dbName))
            throw new ArgumentException("Invalid database name. Must start with a letter and contain only letters, digits, _, -, or .");
    }
}
