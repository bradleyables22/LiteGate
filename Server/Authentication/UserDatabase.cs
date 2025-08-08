using Dapper;
using Microsoft.Data.Sqlite;
using Server.Utiilites;
using System.Text.Json;

namespace Server.Authentication
{
    public class UserDatabase
    {
        private readonly string _connectionString;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public UserDatabase()
        {
            var home = Environment.GetEnvironmentVariable("HOME") ?? AppContext.BaseDirectory;
            var appPath = Path.Combine("databases", "app");
            var dbPath = Path.Combine(home, appPath, "app.db");
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            _connectionString = $"Data Source={dbPath}";
        }


        public async Task<TryResult<bool>> EnsureWalEnabledAsync()
        {
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                var result = await conn.ExecuteScalarAsync<string>("PRAGMA journal_mode = WAL;");
                if (!string.Equals(result, "wal", StringComparison.OrdinalIgnoreCase))
                {
                    return TryResult<bool>.Fail("Failed to enable WAL mode.", new Exception($"Unexpected journal_mode result: {result}"));
                }

                return TryResult<bool>.Pass(true);
            }
            catch (Exception ex)
            {
                return TryResult<bool>.Fail("Exception occurred while enabling WAL mode.", ex);
            }
        }

        public async Task<TryResult<bool>> DeleteUserByIdAsync(string userId)
        {
            await _lock.WaitAsync();
            try
            {
                const string sql = "DELETE FROM Users WHERE Id = @UserId";
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                var rowsAffected = await conn.ExecuteAsync(sql, new { UserId = userId });

                if (rowsAffected == 0)
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

        public async Task<TryResult<AppUser?>> GetUserByIdAsync(string id)
        {
            try
            {
                const string sql = """
                    SELECT *
                    FROM Users
                    WHERE Id = @Id
                """;

                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                var user = await conn.QuerySingleOrDefaultAsync<AppUser>(sql, new { Id = id });

                return TryResult<AppUser?>.Pass(user);
            }
            catch (Exception ex)
            {
                return TryResult<AppUser?>.Fail("Failed to fetch user by ID.", ex);
            }
        }

        public async Task<OffsetTryResult<AppUser>> GetUsersAsync(long skip, int take)
        {
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                var totalSql = "SELECT COUNT(*) FROM Users;";
                var totalCount = await conn.ExecuteScalarAsync<int>(totalSql);

                var dataSql = """
                    SELECT * FROM Users
                    ORDER BY CreatedAt DESC
                    LIMIT @Take OFFSET @Skip;
                """;
                var users = (await conn.QueryAsync<AppUser>(dataSql, new { Take = take, Skip = skip })).ToList();

                return OffsetTryResult<AppUser>.Pass(totalCount, users);
            }
            catch (Exception ex)
            {
                return OffsetTryResult<AppUser>.Fail("Failed to pull user table data.", ex);
            }
        }

        public async Task<TryResult<bool>> EnsureTablesExistAsync()
        {
            await _lock.WaitAsync();
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                var sql = """
                    CREATE TABLE IF NOT EXISTS Users (
                        Id TEXT PRIMARY KEY,
                        UserName TEXT NOT NULL UNIQUE,
                        PasswordHash TEXT NOT NULL,
                        CreatedAt TEXT NOT NULL,
                        DisabledAt TEXT,
                        RolesJson TEXT NOT NULL
                    );
                """;

                await conn.ExecuteAsync(sql);
                return TryResult<bool>.Pass(true);
            }
            catch (Exception ex)
            {
                return TryResult<bool>.Fail("Failed to ensure user table exists.", ex);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<TryResult<bool>> ChangePasswordAsync(string userId, string newPlainTextPassword)
        {
            await _lock.WaitAsync();
            try
            {
                var newHash = PasswordHasher.HashPassword(newPlainTextPassword);

                const string sql = """
                    UPDATE Users
                    SET PasswordHash = @PasswordHash
                    WHERE Id = @UserId
                """;

                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                var rowsAffected = await conn.ExecuteAsync(sql, new { UserId = userId, PasswordHash = newHash });

                if (rowsAffected == 0)
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

        public async Task<TryResult<bool>> UserExistsAsync(string userName)
        {
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                var sql = "SELECT COUNT(*) FROM Users WHERE LOWER(UserName) = LOWER(@User)";
                var count = await conn.ExecuteScalarAsync<long>(sql, new { User = userName });
                return TryResult<bool>.Pass(count > 0);
            }
            catch (Exception ex)
            {
                return TryResult<bool>.Fail("Failed to check if user exists.", ex);
            }
        }

        public async Task<TryResult<bool>> CreateUserAsync(AppUser user, string plainTextPassword)
        {
            await _lock.WaitAsync();
            try
            {
                user.PasswordHash = PasswordHasher.HashPassword(plainTextPassword);
                user.CreatedAt = DateTime.UtcNow;
                user.RolesJson = JsonSerializer.Serialize(user.Roles);

                const string sql = """
                    INSERT INTO Users (Id, UserName, PasswordHash, CreatedAt, DisabledAt, RolesJson)
                    VALUES (@Id, @UserName, @PasswordHash, @CreatedAt, @DisabledAt, @RolesJson);
                """;

                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();
                await conn.ExecuteAsync(sql, user);

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

        public async Task<TryResult<AppUser?>> GetUserByNameAsync(string userName)
        {
            try
            {
                const string sql = """
                    SELECT Id, UserName, PasswordHash, CreatedAt, DisabledAt, RolesJson
                    FROM Users
                    WHERE LOWER(UserName) = LOWER(@User)
                """;

                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                var result = await conn.QuerySingleOrDefaultAsync<AppUser>(sql, new { User = userName });
                return TryResult<AppUser?>.Pass(result);
            }
            catch (Exception ex)
            {
                return TryResult<AppUser?>.Fail("Failed to fetch user.", ex);
            }
        }

        public async Task<TryResult<bool>> UpdateUserRolesAsync(string userId, List<DatabaseRole> newRoles)
        {
            await _lock.WaitAsync();
            try
            {
                var rolesJson = JsonSerializer.Serialize(newRoles);
                var sql = "UPDATE Users SET RolesJson = @RolesJson WHERE Id = @UserId";

                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();
                await conn.ExecuteAsync(sql, new { UserId = userId, RolesJson = rolesJson });

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

        public async Task<TryResult<bool>> DisableUserAsync(string userId)
        {
            await _lock.WaitAsync();
            try
            {
                var sql = "UPDATE Users SET DisabledAt = @DisabledAt WHERE Id = @UserId";

                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();
                await conn.ExecuteAsync(sql, new { UserId = userId, DisabledAt = DateTime.UtcNow });

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
        public async Task<TryResult<bool>> EnableUserAsync(string userId)
        {
            await _lock.WaitAsync();
            try
            {
                var sql = "UPDATE Users SET DisabledAt = NULL WHERE Id = @UserId";

                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();
                await conn.ExecuteAsync(sql, new { UserId = userId });

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
    }
}
