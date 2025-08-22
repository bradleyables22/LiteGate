using Server.Authentication.Models;

namespace Server.Services
{
    public class SystemSeeder : BackgroundService
    {
        private readonly UserDatabase _db;

        public SystemSeeder(UserDatabase db)
        {
            _db = db;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                DirectoryManager.EnsureDatabaseFolder("app");
                await _db.EnsureTablesExistAsync();
                await _db.EnsureWalEnabledAsync();

                const string defaultUserName = "SuperAdmin";
                const string defaultPassword = "ChangeDisPassword123!";
                var userExistsResult = await _db.UserExistsAsync(defaultUserName);
                if (!userExistsResult.Data)
                {
                    var user = new AppUser
                    {
                        UserName = defaultUserName,
                        Roles = new List<DatabaseRole>
                        {
                            new() { Database = "*", Role = SystemRole.admin }
                        }
                    };
                    await _db.CreateUserAsync(user, defaultPassword);
                }

                await _db.TruncateWalAsync();

            }
            catch (Exception ex)
            {
                
            }
        }
    }
}