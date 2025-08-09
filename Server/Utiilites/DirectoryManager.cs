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

    public static string BuildSqliteConnectionString(string dbName)
    {
        EnsureDatabaseFolder(dbName);
        var dbPath = GetDatabaseFile(dbName);
        return $"Data Source={dbPath};Default Timeout=5;";
    }

    public static IReadOnlyList<string> ListDatabases()
    {
        var root = GetRoot();
        if (!Directory.Exists(root)) return Array.Empty<string>();

        var names = new List<string>();
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(dir);
            if (!SafeName.IsMatch(name)) continue;

            var dbFile = Path.Combine(dir, $"{name}.db");
            if (File.Exists(dbFile)) names.Add(name);
        }
        return names;
    }

    public static bool DeleteDatabase(string dbName)
    {
        var folder = GetDatabaseFolder(dbName);
        if (!Directory.Exists(folder)) return false;

        Directory.Delete(folder, recursive: true); // nukes db, wal, shm
        return true;
    }

    public static void CopyDatabase(string sourceName, string destName, bool overwrite = false)
    {
        var src = GetDatabaseFolder(sourceName);
        if (!Directory.Exists(src))
            throw new DirectoryNotFoundException($"Source database folder not found: {src}");

        var dst = GetDatabaseFolder(destName);

        if (Directory.Exists(dst))
        {
            if (!overwrite)
                throw new IOException($"Destination exists: {dst}");
            Directory.Delete(dst, recursive: true);
        }

        Directory.CreateDirectory(dst);

        foreach (var file in Directory.EnumerateFiles(src))
        {
            var destFile = Path.Combine(dst, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }
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
        var dbFile = GetDatabaseFile(dbName);
        if (!File.Exists(dbFile))
            throw new FileNotFoundException("Database file not found.", dbFile);

        await using var fs = new FileStream(dbFile, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        using var ms = new MemoryStream();
        await fs.CopyToAsync(ms);
        return ms.ToArray();
    }

    public static byte[] GetDatabaseZip(string dbName)
    {
        var folder = GetDatabaseFolder(dbName);
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"Database folder not found: {folder}");

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var filePath in Directory.EnumerateFiles(folder))
            {
                var entryName = Path.GetFileName(filePath);
                var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                using var fileStream = File.OpenRead(filePath);
                fileStream.CopyTo(entryStream);
            }
        }
        return ms.ToArray();
    }

    private static void ValidateName(string dbName)
    {
        if (string.IsNullOrWhiteSpace(dbName))
            throw new ArgumentException("Database name is required.", nameof(dbName));
        if (!SafeName.IsMatch(dbName))
            throw new ArgumentException("Invalid database name. Must start with a letter and contain only letters, digits, _, -, or .");
    }
}
