using Dapper;
using Microsoft.Data.Sqlite;
using Server.Utiilites;
using System.Text.Json;

namespace Server.Authentication
{
    public class UserDatabase
    {
        private readonly string _connectionString = "Data Source=data/app.db";

        public async Task<TryResult<bool>> EnsureTablesExistAsync()
        {
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
        }
    }
}
