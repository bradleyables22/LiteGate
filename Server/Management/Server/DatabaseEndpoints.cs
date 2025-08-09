using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Server.Management.Server
{
    public static class DatabaseEndpoints
    {
        public static WebApplication MapDatabaseManagementEndpoints(this WebApplication app) 
        {
            var databaseGroup = app.MapGroup("api/v1/datbases").WithTags("Database Management");

            databaseGroup.MapGet("", () =>
            {
                return Results.Ok(DirectoryManager.ListDatabases());
            })
            .RequireAuthorization(policy => policy.RequireRole("*:admin", "*:owner", "*:editor", "*:viewer", "app.db:admin", "app.db:owner", "app.db:editor", "app.db:viewer"))
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
            .RequireAuthorization(policy => policy.RequireRole("*:admin", "*:owner", "*:editor", "*:viewer", "app.db:admin", "app.db:owner", "app.db:editor", "app.db:viewer"))
            .Produces<FileContentResult>(200)
            .WithOpenApi()
            .WithDisplayName("DownloadDatabase")
            .WithName("DownloadDatabase")
            .WithDescription("Download a database file by name.")
            .WithSummary("Download Database");

            databaseGroup.MapPost("/copy/{name}", ([FromRoute] string name, [FromQuery] string newName) =>
            {
                if (name.ToLower() == "app.db" || name.ToLower() == "app")
                    return Results.Forbid();

                if (name.ToLower() == newName.ToLower())
                    return Results.BadRequest("Paths cannot match");

                var originalExists = DirectoryManager.DatabaseFileExists(name);
                
                if (originalExists)
                {
                    var newExists = DirectoryManager.DatabaseFileExists(newName);
                    if (newExists)
                        return Results.BadRequest("Directory already exists");

                    DirectoryManager.CopyDatabase(name,newName);
                    return Results.Ok();
                }
                else
                    return Results.NotFound();

                    
            })
            .RequireAuthorization(policy => policy.RequireRole("*:admin", "*:owner"))
            .Produces(200)
            .WithOpenApi()
            .WithDisplayName("CopyDatabase")
            .WithName("CopyDatabase")
            .WithDescription("Copy a database to a new file name.")
            .WithSummary("Copy Database");

            databaseGroup.MapDelete("/{name}", (string name) =>
            {
                if (name.ToLower() == "app.db" || name.ToLower() == "app")
                    return Results.Forbid();

                var exists = DirectoryManager.DatabaseFileExists(name);
                if (exists)
                {
                    var deleted = DirectoryManager.DeleteDatabase(name);
                    return deleted ? Results.Ok() : Results.NotFound();
                }
                else
                    return Results.NotFound();
            })
            .RequireAuthorization(policy => policy.RequireRole("*:admin", "*:owner"))
            .Produces(200)
            .WithOpenApi()
            .WithDisplayName("DeleteDatabase")
            .WithName("DeleteDatabase")
            .WithDescription("Delete a database file and its WAL/SHM.")
            .WithSummary("Delete Database");
            return app;
        }
    }
}
