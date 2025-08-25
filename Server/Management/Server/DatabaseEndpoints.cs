using Microsoft.AspNetCore.Mvc;
using Server.Authentication.Models;
using Server.Interaction;
using Server.Services;
using Server.Utilities;

namespace Server.Management.Server
{
    public static class DatabaseEndpoints
    {
        public static WebApplication MapDatabaseManagementEndpoints(this WebApplication app) 
        {
            var databaseGroup = app.MapGroup("api/v1/databasemanagement").WithTags("Database Management");

            databaseGroup.MapGet("", () =>
            {
                return Results.Ok(DirectoryManager.ListDatabases());
            })
            .RequireAuthorization(policy => policy.RequireRole("*:admin", "*:editor", "*:viewer", "app:admin", "app:editor", "app:viewer"))
            .Produces<IReadOnlyList<DatabaseLookupResult>>(200)
            .WithOpenApi()
            .WithDisplayName("ListDatabases")
            .WithName("ListDatabases")
            .WithDescription("List all database files.")
            .WithSummary("List");

            databaseGroup.MapPut("/overwrite/{name}", async (DatabaseGateManager _gateManager, HttpContext _context, string name) =>
            {
                if (name.ToLower() == "app.db" || name.ToLower() == "app")
                    return Results.Forbid();

                if (!DirectoryManager.DatabaseFileExists(name))
                    return Results.NotFound($"Database {name} not found.");

                if (name.ToLower().Contains("."))
                    return Results.BadRequest("Do not specify filetype");

                var userPermissions = _context.ExtractAllowedPermissions(name);

                if (userPermissions is null || !userPermissions.Any() || !userPermissions.Any(x => x == SystemRole.admin))
                    return Results.Forbid();

                var folder = DirectoryManager.GetDatabaseFolder(name);
                var dbFile = DirectoryManager.GetDatabaseFile(name);

                DirectoryManager.EnsureDatabaseFolder(name);

                var tempFile = Path.Combine(folder, $"{Guid.NewGuid()}.tmp");
                
                await using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await _context.Request.Body.CopyToAsync(fs);
                }

                var walFile = Path.ChangeExtension(dbFile, ".db-wal");
                var shmFile = Path.ChangeExtension(dbFile, ".db-shm");
                
                if (File.Exists(walFile)) 
                    File.Delete(walFile);

                if (File.Exists(shmFile)) 
                    File.Delete(shmFile);
                
                File.Move(tempFile, dbFile, overwrite: true);

                _ = await _gateManager.EnsureWalEnabledAsync(name);
                
                await _gateManager.PrimeNewDatabaseAsync(name);

                return Results.Ok();
            })
            .RequireAuthorization()
            .Produces(200)
            .WithOpenApi()
            .WithDisplayName("OverwriteDatabase")
            .WithName("OverwriteDatabase")
            .WithDescription("Overwrite an existing database file by name. Ensures WAL is enabled after replacement.")
            .WithSummary("Overwrite");

            databaseGroup.MapPut("/truncate/{name}", async (DatabaseGateManager _gateManager, HttpContext _context, string name) =>
            {

                if (!DirectoryManager.DatabaseFileExists(name))
                    return Results.NotFound($"Database {name} not found.");

                if (name.ToLower().Contains("."))
                    return Results.BadRequest("Do not specify filetype");

                var userPermissions = _context.ExtractAllowedPermissions(name);

                if (userPermissions is null || !userPermissions.Any() || !userPermissions.Any(x => x == SystemRole.admin))
                    return Results.Forbid();

               var result = await _gateManager.TruncateWalAsync(name);
                if (result.Success)
                    return Results.Ok();
                else
                    return result.ToResult();
            })
            .RequireAuthorization()
            .Produces(200)
            .WithOpenApi()
            .WithDisplayName("TruncateDatabase")
            .WithName("TruncateDatabase")
            .WithDescription("Manually truncate a database WAL file into the main database.")
            .WithSummary("Truncate");

            databaseGroup.MapPut("/vacuum/{name}", async (DatabaseGateManager _gateManager, HttpContext _context, string name) =>
            {
                if (name.ToLower() == "app.db" || name.ToLower() == "app")
                    return Results.Forbid();

                if (!DirectoryManager.DatabaseFileExists(name))
                    return Results.NotFound($"Database {name} not found.");

                if (name.ToLower().Contains("."))
                    return Results.BadRequest("Do not specify filetype");

                var userPermissions = _context.ExtractAllowedPermissions(name);

                if (userPermissions is null || !userPermissions.Any() || !userPermissions.Any(x => x == SystemRole.admin))
                    return Results.Forbid();

                var vacResult = await _gateManager.VacuumAsync(name);
                if (vacResult.Success)
                {
                    _ = _gateManager.PrimeNewDatabaseAsync(name);

                    return Results.Ok();
                }
                else
                    return vacResult.ToResult();

            })
           .RequireAuthorization()
           .Produces(200)
           .WithOpenApi()
           .WithDisplayName("VacuumDatabase")
           .WithName("VacuumDatabase")
           .WithDescription("Manually vacuum a database.")
           .WithSummary("Vacuum");

            databaseGroup.MapGet("/download/{name}", async (DatabaseGateManager _gateManager,HttpContext _context, string name) =>
            {
                if (name.ToLower().Contains("."))
                    return Results.BadRequest("Do not specify filetype");
                var exists = DirectoryManager.DatabaseFileExists(name);
                if (exists)
                {
                    var userPermissions = _context.ExtractAllowedPermissions(name);

                    if (userPermissions is null || !userPermissions.Any())
                        return Results.Forbid();

                    _ = await _gateManager.TruncateWalAsync(name);
                    var bytes = await DirectoryManager.GetDatabaseBytesAsync(name);
                    return Results.File(bytes, "application/octet-stream", name);
                }
                else
                    return Results.NotFound();
                    
            })
            .RequireAuthorization()
            .Produces<FileContentResult>(200)
            .WithOpenApi()
            .WithDisplayName("DownloadDatabase")
            .WithName("DownloadDatabase")
            .WithDescription("Download a database file by name.")
            .WithSummary("Download");

            databaseGroup.MapPost("/create/{name}", async ([FromRoute] string name, DatabaseGateManager _gateManager) =>
            {
                if (name.ToLower() == "app.db" || name.ToLower() == "app")
                    return Results.Forbid();

                if (name.ToLower().Contains("."))
                    return Results.BadRequest("Do not specify filetype");
                try
                {
                    DirectoryManager.ValidateName(name);
                }
                catch (Exception)
                {
                    return Results.BadRequest("Bad file name");
                }

                var exists = DirectoryManager.DatabaseFileExists(name);
                if (exists)
                    return Results.Conflict("File exists");

                DirectoryManager.EnsureDatabaseFolder(name);
                var fileName = DirectoryManager.GetDatabaseFile(name);

                _= await _gateManager.EnsureWalEnabledAsync(name);
               await _gateManager.PrimeNewDatabaseAsync(name);

                return Results.Ok();
            })
            .RequireAuthorization(policy => policy.RequireRole("*:admin","app:admin"))
            .Produces(200)
            .WithOpenApi()
            .WithDisplayName("CreateDatabase")
            .WithName("CreateDatabase")
            .WithDescription("Create a database file")
            .WithSummary("Create");

            databaseGroup.MapDelete("/{name}", async (string name, HttpContext _context, UserDatabase _db) =>
            {
                if (name.ToLower() == "app.db" || name.ToLower() == "app")
                    return Results.Forbid();

                if (name.ToLower().Contains("."))
                    return Results.BadRequest("Do not specify filetype");


                var userPermissions = _context.ExtractAllowedPermissions(name);

                if (userPermissions is null || !userPermissions.Any() || !userPermissions.Any(x => x == SystemRole.admin))
                    return Results.Forbid();

                var exists = DirectoryManager.DatabaseFileExists(name);
                if (exists)
                {
                    var deleted = DirectoryManager.DeleteDatabase(name);

                    if (deleted)
                    {
                        var usersResult = await _db.GetAllUsersAsync();
                        if (usersResult.Success && usersResult.Data is not null && usersResult.Data.Any())
                        {
                            foreach (var user in usersResult.Data)
                            {
                                var applicableRoles = user.Roles.Where(x=>x.Database.ToLower() == name.ToLower()).ToList();

                                if (applicableRoles is not null && applicableRoles.Any() == true)
                                {
                                    foreach (var role in applicableRoles)
                                    {
                                        user.Roles.Remove(role);
                                    }
                                }

                                _ = await _db.UpdateUserRolesAsync(user.Id, user.Roles);

                            }
                        }
                    }

                    return deleted ? Results.Ok() : Results.NotFound();
                }
                else
                    return Results.NotFound();
            })
            .RequireAuthorization(policy => policy.RequireRole("*:admin", "app:admin"))
            .Produces(200)
            .WithOpenApi()
            .WithDisplayName("DeleteDatabase")
            .WithName("DeleteDatabase")
            .WithDescription("Delete a database file.")
            .WithSummary("Delete");
            return app;
        }
    }
}
