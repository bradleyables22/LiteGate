using Microsoft.Data.Sqlite;
using Server.Authentication;
using Server.Authentication.Models;
using Server.Utilities;
using System.Data.SQLite;
using System.Globalization;
using System.Text.Json;

namespace Server.Services
{
    public class UserDatabase
    {
        private readonly string _connectionString;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public UserDatabase()
        {
            DirectoryManager.EnsureDatabaseFolder("app");
            _connectionString = DirectoryManager.BuildSqliteConnectionString("app");
        }

        public async Task<TryResult<bool>> EnsureWalEnabledAsync(CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct);
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(ct);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA journal_mode = WAL;";
                var result = (await cmd.ExecuteScalarAsync(ct))?.ToString();

                if (!string.Equals(result, "wal", StringComparison.OrdinalIgnoreCase))
                    return TryResult<bool>.Fail("Failed to enable WAL mode.", new Exception($"Unexpected journal_mode result: {result}"));

                return TryResult<bool>.Pass(true);
            }
            catch (Exception ex)
            {
                return TryResult<bool>.Fail("Exception occurred while enabling WAL mode.", ex);
            }
            finally 
            {
                _lock.Release();
            }
        }
        
        public async Task<TryResult<bool>> TruncateWalAsync(CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct);
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(ct);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                var result = await cmd.ExecuteScalarAsync(ct);

                return TryResult<bool>.Pass(true);
            }
            catch (Exception ex)
            {
                return TryResult<bool>.Fail("Failed to truncate WAL.", ex);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<TryResult<bool>> DeleteUserByIdAsync(string userId, CancellationToken ct = default)
        {
            await _lock.WaitAsync();
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(ct);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM Users WHERE Id = @UserId";
                cmd.Parameters.AddWithValue("@UserId", userId);
                var rows = await cmd.ExecuteNonQueryAsync(ct);

                if (rows == 0)
                    return TryResult<bool>.Fail("No user found with the given ID.", new KeyNotFoundException());

                return TryResult<bool>.Pass(true);
            }
            catch (Exception ex)
            {
                return TryResult<bool>.Fail("Failed to delete user.", ex);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<TryResult<AppUser?>> GetUserByIdAsync(string id, CancellationToken ct = default)
        {
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(ct);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT * FROM Users WHERE Id = @Id";
                cmd.Parameters.AddWithValue("@Id", id);

                using var reader = await cmd.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                    return TryResult<AppUser?>.Pass(MapUser(reader));

                return TryResult<AppUser?>.Pass(null);
            }
            catch (Exception ex)
            {
                return TryResult<AppUser?>.Fail("Failed to fetch user by ID.", ex);
            }
        }

        public async Task<TryResult<List<AppUser>>> GetAllUsersAsync(CancellationToken ct = default) 
        {
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(ct);

                int totalCount;
                using (var countCmd = conn.CreateCommand())
                {
                    countCmd.CommandText = "SELECT COUNT(*) FROM Users;";
                    totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));
                }

                var users = new List<AppUser>();
                using (var dataCmd = conn.CreateCommand())
                {
                    dataCmd.CommandText = "SELECT * FROM Users";

                    using var reader = await dataCmd.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct))
                        users.Add(MapUser(reader));
                }

                return TryResult<List<AppUser>>.Pass(users);

            }
            catch (Exception e)
            {
                return TryResult<List<AppUser>>.Fail(e.Message, e);
            }
        }
        public async Task<OffsetTryResult<AppUser>> GetUsersByOffsetAsync(long skip, int take, CancellationToken ct = default)
        {
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(ct);

                int totalCount;
                using (var countCmd = conn.CreateCommand())
                {
                    countCmd.CommandText = "SELECT COUNT(*) FROM Users;";
                    totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));
                }

                var users = new List<AppUser>();
                using (var dataCmd = conn.CreateCommand())
                {
                    dataCmd.CommandText = "SELECT * FROM Users ORDER BY CreatedAt DESC LIMIT @Take OFFSET @Skip;";
                    dataCmd.Parameters.AddWithValue("@Take", take);
                    dataCmd.Parameters.AddWithValue("@Skip", skip);

                    using var reader = await dataCmd.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct))
                        users.Add(MapUser(reader));
                }

                return OffsetTryResult<AppUser>.Pass(totalCount, users);
            }
            catch (Exception ex)
            {
                return OffsetTryResult<AppUser>.Fail("Failed to pull user table data.", ex);
            }
        }

        public async Task<TryResult<bool>> EnsureTablesExistAsync(CancellationToken ct = default)
        {
            await _lock.WaitAsync();
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(ct);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE IF NOT EXISTS Users (
                        Id TEXT PRIMARY KEY,
                        UserName TEXT NOT NULL UNIQUE,
                        PasswordHash TEXT NOT NULL,
                        CreatedAt TEXT NOT NULL,
                        DisabledAt TEXT,
                        RolesJson TEXT NOT NULL
                    );
                    CREATE TABLE SubscriptionRecords (
                        Id TEXT PRIMARY KEY NOT NULL,
                        UserId TEXT NOT NULL,
                        Url TEXT NOT NULL,
                        DatabaseName TEXT NOT NULL,
                        TableName TEXT NOT NULL,
                        Secret TEXT NOT NULL,
                        CreatedAt TEXT NOT NULL,
                        Event INTEGER NOT NULL,
                        FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE
                    );
                CREATE UNIQUE INDEX IF NOT EXISTS UX_Subscription_UserDbTableEvent
                ON SubscriptionRecords(UserId, DatabaseName, TableName, Event);

                CREATE INDEX IF NOT EXISTS IX_Subscription_UserId ON SubscriptionRecords(UserId);
                CREATE INDEX IF NOT EXISTS IX_Subscription_CreatedAt ON SubscriptionRecords(CreatedAt);
                """;
                await cmd.ExecuteNonQueryAsync(ct);
                return TryResult<bool>.Pass(true);
            }
            catch (Exception ex)
            {
                return TryResult<bool>.Fail("Failed to ensure tables exists.", ex);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<TryResult<bool>> ChangePasswordAsync(string userId, string newPlainTextPassword, CancellationToken ct = default)
        {
            await _lock.WaitAsync();
            try
            {
                var newHash = PasswordHasher.HashPassword(newPlainTextPassword);
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(ct);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE Users SET PasswordHash = @PasswordHash WHERE Id = @UserId";
                cmd.Parameters.AddWithValue("@PasswordHash", newHash);
                cmd.Parameters.AddWithValue("@UserId", userId);

                var rows = await cmd.ExecuteNonQueryAsync(ct);
                if (rows == 0)
                    return TryResult<bool>.Fail("User not found or password unchanged.", new Exception("No rows affected."));

                return TryResult<bool>.Pass(true);
            }
            catch (Exception ex)
            {
                return TryResult<bool>.Fail("Failed to change user password.", ex);
            }
            finally
            {
                _lock.Release();
            }
        }
      
        public async Task<TryResult<bool>> UserExistsAsync(string userName, CancellationToken ct = default)
        {
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(ct);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM Users WHERE LOWER(UserName) = LOWER(@User)";
                cmd.Parameters.AddWithValue("@User", userName);

                var count = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
                return TryResult<bool>.Pass(count > 0);
            }
            catch (Exception ex)
            {
                return TryResult<bool>.Fail("Failed to check if user exists.", ex);
            }
        }
       
        public async Task<TryResult<bool>> CreateUserAsync(AppUser user, string plainTextPassword, CancellationToken ct = default)
        {
            await _lock.WaitAsync();
            try
            {
                user.PasswordHash = PasswordHasher.HashPassword(plainTextPassword);
                user.CreatedAt = DateTime.UtcNow;
                user.RolesJson = JsonSerializer.Serialize(user.Roles);

                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO Users (Id, UserName, PasswordHash, CreatedAt, RolesJson)
                    VALUES (@Id, @UserName, @PasswordHash, @CreatedAt, @RolesJson);
                """;
                cmd.Parameters.AddWithValue("@Id", user.Id);
                cmd.Parameters.AddWithValue("@UserName", user.UserName);
                cmd.Parameters.AddWithValue("@PasswordHash", user.PasswordHash);
                cmd.Parameters.AddWithValue("@CreatedAt", user.CreatedAt.ToUniversalTime().ToString("o"));
                cmd.Parameters.AddWithValue("@RolesJson", user.RolesJson);

                await cmd.ExecuteNonQueryAsync();
                return TryResult<bool>.Pass(true);
            }
            catch (Exception ex)
            {
                return TryResult<bool>.Fail("Failed to create user.", ex);
            }
            finally
            {
                _lock.Release();
            }
        }
       
        public async Task<TryResult<AppUser?>> GetUserByNameAsync(string userName, CancellationToken ct = default)
        {
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(ct);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT * FROM Users WHERE LOWER(UserName) = LOWER(@User)";
                cmd.Parameters.AddWithValue("@User", userName);

                using var reader = await cmd.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync())
                    return TryResult<AppUser?>.Pass(MapUser(reader));

                

                return TryResult<AppUser?>.Pass(null);
            }
            catch (Exception ex)
            {
                return TryResult<AppUser?>.Fail("Failed to fetch user.", ex);
            }
        }
        
        public async Task<TryResult<bool>> UpdateUserRolesAsync(string userId, List<DatabaseRole> newRoles, CancellationToken ct = default)
        {
            await _lock.WaitAsync();
            try
            {
                var rolesJson = JsonSerializer.Serialize(newRoles);
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(ct);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE Users SET RolesJson = @RolesJson WHERE Id = @UserId";
                cmd.Parameters.AddWithValue("@RolesJson", rolesJson);
                cmd.Parameters.AddWithValue("@UserId", userId);
                await cmd.ExecuteNonQueryAsync(ct);
                return TryResult<bool>.Pass(true);
            }
            catch (Exception ex)
            {
                return TryResult<bool>.Fail("Failed to update user roles.", ex);
            }
            finally
            {
                _lock.Release();
            }
        }
       
        public async Task<TryResult<bool>> DisableUserAsync(string userId, CancellationToken ct = default)
        {
            await _lock.WaitAsync();
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(ct);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE Users SET DisabledAt = @DisabledAt WHERE Id = @UserId";
                cmd.Parameters.AddWithValue("@DisabledAt", DateTime.UtcNow.ToUniversalTime().ToString("o"));
                cmd.Parameters.AddWithValue("@UserId", userId);
                await cmd.ExecuteNonQueryAsync(ct);
                return TryResult<bool>.Pass(true);
            }
            catch (Exception ex)
            {
                return TryResult<bool>.Fail("Failed to disable user.", ex);
            }
            finally
            {
                _lock.Release();
            }
        }
       
        public async Task<TryResult<bool>> EnableUserAsync(string userId, CancellationToken ct = default)
        {
            await _lock.WaitAsync();
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(ct);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE Users SET DisabledAt = NULL WHERE Id = @UserId";
                cmd.Parameters.AddWithValue("@UserId", userId);
                await cmd.ExecuteNonQueryAsync(ct);
                return TryResult<bool>.Pass(true);
            }
            catch (Exception ex)
            {
                return TryResult<bool>.Fail("Failed to enable user.", ex);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<TryResult<SubscriptionRecord?>> GetSubscriptionByIdAsync(string id, CancellationToken ct = default)
        {
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(ct);

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT * FROM SubscriptionRecords WHERE Id = @Id;";
                cmd.Parameters.AddWithValue("@Id", id);

                using var reader = await cmd.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    var subscription = MapSubscription(reader);
                    return TryResult<SubscriptionRecord?>.Pass(subscription);
                }

                return TryResult<SubscriptionRecord?>.Pass(null); 
            }
            catch (Exception ex)
            {
                return TryResult<SubscriptionRecord?>.Fail("Failed to fetch subscription.", ex);
            }
        }

        public async Task<TryResult<List<SubscriptionRecord>>> GetSubscriptionsByKeysAsync(string database, string table,UpdateEventType evnt,  CancellationToken ct = default)
        {
            try
            {

                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(ct);

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT Id, UserId, Url, DatabaseName, TableName, Secret, CreatedAt, Event
                    FROM SubscriptionRecords
                    WHERE LOWER(DatabaseName) = @db
                      AND LOWER(TableName) = @tbl
                      AND Event = @evt
                ";
                cmd.Parameters.AddWithValue("@db", database);
                cmd.Parameters.AddWithValue("@tbl", table);
                cmd.Parameters.AddWithValue("@evt", (int)evnt);

                var list = new List<SubscriptionRecord>();
                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    list.Add(MapSubscription(reader)); 
                }

                return TryResult<List<SubscriptionRecord>>.Pass(list);
            }
            catch (Exception ex)
            {
                return TryResult<List<SubscriptionRecord>>.Fail(ex.Message,ex);
            }
        }

        public async Task<OffsetTryResult<SubscriptionRecord>> GetSubscriptionsAsync(long skip, int take, CancellationToken ct = default)
        {
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(ct);

                int totalCount;
                using (var countCmd = conn.CreateCommand())
                {
                    countCmd.CommandText = "SELECT COUNT(*) FROM SubscriptionRecords;";
                    totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));
                }

                var items = new List<SubscriptionRecord>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = """
                        SELECT * FROM SubscriptionRecords
                        ORDER BY CreatedAt DESC
                        LIMIT @Take OFFSET @Skip;
                    """;
                    cmd.Parameters.AddWithValue("@Take", take);
                    cmd.Parameters.AddWithValue("@Skip", skip);

                    using var reader = await cmd.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct))
                        items.Add(MapSubscription(reader));
                }

                return OffsetTryResult<SubscriptionRecord>.Pass(totalCount, items);
            }
            catch (Exception ex)
            {
                return OffsetTryResult<SubscriptionRecord>.Fail("Failed to fetch subscriptions.", ex);
            }
        }

        public async Task<OffsetTryResult<SubscriptionRecord>> GetSubscriptionsByUserAsync(string userId, long skip, int take, CancellationToken ct = default)
        {
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(ct);

                int totalCount;
                using (var countCmd = conn.CreateCommand())
                {
                    countCmd.CommandText = "SELECT COUNT(*) FROM SubscriptionRecords WHERE UserId = $UserId;";
                    countCmd.Parameters.AddWithValue("$UserId", userId);
                    totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));
                }

                var items = new List<SubscriptionRecord>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = """
                        SELECT * FROM SubscriptionRecords
                        WHERE UserId = @UserId
                        ORDER BY CreatedAt DESC
                        LIMIT @Take OFFSET @Skip;
                    """;
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    cmd.Parameters.AddWithValue("@Take", take);
                    cmd.Parameters.AddWithValue("@Skip", skip);

                    using var reader = await cmd.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct))
                        items.Add(MapSubscription(reader));
                }

                return OffsetTryResult<SubscriptionRecord>.Pass(totalCount, items);
            }
            catch (Exception ex)
            {
                return OffsetTryResult<SubscriptionRecord>.Fail("Failed to fetch user subscriptions.", ex);
            }
        }

        public async Task<TryResult<bool>> CreateSubscriptionAsync(SubscriptionRecord sub, CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct);
            try
            {
                sub.Id = string.IsNullOrWhiteSpace(sub.Id) ? Guid.CreateVersion7().ToString() : sub.Id;
                sub.CreatedAt = sub.CreatedAt == default ? DateTime.UtcNow : sub.CreatedAt;

                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(ct);

                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO SubscriptionRecords
                        (Id, UserId, Url, DatabaseName, TableName, Secret, CreatedAt, Event)
                    VALUES
                        (@Id, @UserId, @Url, @DatabaseName, @TableName, @Secret, @CreatedAt, @Event)
                    ON CONFLICT(UserId, DatabaseName, TableName, Event) DO NOTHING;
                """;
                cmd.Parameters.AddWithValue("@Id", sub.Id);
                cmd.Parameters.AddWithValue("@UserId", sub.UserId);
                cmd.Parameters.AddWithValue("@Url", sub.Url);
                cmd.Parameters.AddWithValue("@DatabaseName", sub.Database);
                cmd.Parameters.AddWithValue("@TableName", sub.Table);
                cmd.Parameters.AddWithValue("@Secret", sub.Secret);
                cmd.Parameters.AddWithValue("@CreatedAt", sub.CreatedAt.ToUniversalTime().ToString("o"));
                cmd.Parameters.AddWithValue("@Event", (int)sub.Event);

                var rows = await cmd.ExecuteNonQueryAsync(ct);
                if (rows == 0)
                    return TryResult<bool>.Fail("Subscription already exists for this (UserId, Database, Table, Event).",new Exception());

                return TryResult<bool>.Pass(true);
            }
            catch (Exception ex)
            {
                return TryResult<bool>.Fail("Failed to create subscription.", ex);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<TryResult<bool>> DeleteSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct);
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(ct);

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM SubscriptionRecords WHERE Id = @Id;";
                cmd.Parameters.AddWithValue("@Id", subscriptionId);

                var rows = await cmd.ExecuteNonQueryAsync(ct);
                if (rows == 0)
                    return TryResult<bool>.Fail("No subscription found with the given Id.", new Exception());

                return TryResult<bool>.Pass(true);
            }
            catch (Exception ex)
            {
                return TryResult<bool>.Fail("Failed to delete subscription.", ex);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<TryResult<bool>> ClearSubscriptionsByUserAsync(string userId, CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct);
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(ct);

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM SubscriptionRecords WHERE UserId = @UserId;";
                cmd.Parameters.AddWithValue("@UserId", userId);

                await cmd.ExecuteNonQueryAsync(ct);
                return TryResult<bool>.Pass(true);
            }
            catch (Exception ex)
            {
                return TryResult<bool>.Fail("Failed to clear subscriptions for user.", ex);
            }
            finally
            {
                _lock.Release();
            }
        }

        DateTime ParseIso8601Utc(string s)
        {
            var dto = DateTimeOffset.ParseExact(s, "o", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
            return dto.UtcDateTime;
        }

        AppUser MapUser(SqliteDataReader reader)
        {
            var idOrd = reader.GetOrdinal("Id");
            var userNameOrd = reader.GetOrdinal("UserName");
            var pwdHashOrd = reader.GetOrdinal("PasswordHash");
            var createdAtOrd = reader.GetOrdinal("CreatedAt");
            var disabledAtOrd = reader.GetOrdinal("DisabledAt");
            var rolesJsonOrd = reader.GetOrdinal("RolesJson");

            var createdAtUtc = ParseIso8601Utc(reader.GetString(createdAtOrd));

            DateTime? disabledAtUtc = null;
            if (!reader.IsDBNull(disabledAtOrd))
                disabledAtUtc = ParseIso8601Utc(reader.GetString(disabledAtOrd));

            return new AppUser
            {
                Id = reader.GetString(idOrd),
                UserName = reader.GetString(userNameOrd),
                PasswordHash = reader.GetString(pwdHashOrd),
                CreatedAt = createdAtUtc,          
                DisabledAt = disabledAtUtc,        
                RolesJson = reader.GetString(rolesJsonOrd),
            };
        }

        SubscriptionRecord MapSubscription(SqliteDataReader reader)
        {
            var idOrd = reader.GetOrdinal("Id");
            var userIdOrd = reader.GetOrdinal("UserId");
            var urlOrd = reader.GetOrdinal("Url");
            var dbOrd = reader.GetOrdinal("DatabaseName");
            var tableOrd = reader.GetOrdinal("TableName");
            var secretOrd = reader.GetOrdinal("Secret");
            var createdAtOrd = reader.GetOrdinal("CreatedAt");
            var eventOrd = reader.GetOrdinal("Event");

            return new SubscriptionRecord
            {
                Id = reader.GetString(idOrd),
                UserId = reader.GetString(userIdOrd),
                Url = reader.GetString(urlOrd),
                Database = reader.GetString(dbOrd),
                Table = reader.GetString(tableOrd),
                Secret = reader.GetString(secretOrd),
                CreatedAt = ParseIso8601Utc(reader.GetString(createdAtOrd)),
                Event = (UpdateEventType)reader.GetInt32(eventOrd)
            };
        }
    }
}
