using Dapper;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Server.Utiilites;

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
            .RequireAuthorization(policy => policy.RequireRole("*:admin", "*:editor", "*:viewer", "app.db:admin", "app.db:editor", "app.db:viewer"))
            .Produces<IReadOnlyList<string>>(200)
            .WithOpenApi()
            .WithDisplayName("ListDatabases")
            .WithName("ListDatabases")
            .WithDescription("List all database files.")
            .WithSummary("List Databases");

            databaseGroup.MapGet("/download/{name}", async (string name) =>
            {

                var exists = DirectoryManager.DatabaseFileExists(name);
                if (exists)
                {
                    var bytes = await DirectoryManager.GetDatabaseBytesAsync(name);
                    return Results.File(bytes, "application/octet-stream", name);
                }
                else
                    return Results.NotFound();
                    
            })
            .RequireAuthorization(policy => policy.RequireRole("*:admin", "*:editor", "*:viewer", "app.db:admin", "app.db:editor", "app.db:viewer"))
            .Produces<FileContentResult>(200)
            .WithOpenApi()
            .WithDisplayName("DownloadDatabase")
            .WithName("DownloadDatabase")
            .WithDescription("Download a database file by name.")
            .WithSummary("Download Database");

            databaseGroup.MapPost("/create/{name}", async ([FromRoute] string name) =>
            {
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

                var connectionString = DirectoryManager.BuildSqliteConnectionString(name);

                using var conn = new SqliteConnection(connectionString);
                await conn.OpenAsync();

                var result = await conn.ExecuteScalarAsync<string>("PRAGMA journal_mode = WAL;");
                if (!string.Equals(result, "wal", StringComparison.OrdinalIgnoreCase))
                    return Results.Problem(detail: "Issue activating WAL",title: "Error",statusCode: StatusCodes.Status500InternalServerError);

                return Results.Ok();
            })
            .RequireAuthorization(policy => policy.RequireRole("*:admin","app.db:admin"))
            .Produces(200)
            .WithOpenApi()
            .WithDisplayName("CreateDatabase")
            .WithName("CreateDatabase")
            .WithDescription("Create a database file")
            .WithSummary("Create Database");

            databaseGroup.MapDelete("/{name}", (string name) =>
            {
                if (name.ToLower() == "app.db" || name.ToLower() == "app")
                    return Results.Forbid();

                if (name.ToLower().Contains("."))
                    return Results.BadRequest("Do not specify filetype");

                var exists = DirectoryManager.DatabaseFileExists(name);
                if (exists)
                {
                    var deleted = DirectoryManager.DeleteDatabase(name);
                    return deleted ? Results.Ok() : Results.NotFound();
                }
                else
                    return Results.NotFound();
            })
            .RequireAuthorization(policy => policy.RequireRole("*:admin", "app.db:admin"))
            .Produces(200)
            .WithOpenApi()
            .WithDisplayName("DeleteDatabase")
            .WithName("DeleteDatabase")
            .WithDescription("Delete a database file.")
            .WithSummary("Delete Database");
            return app;
        }
    }
}
