using Microsoft.AspNetCore.Mvc;
using Server.Services;
using Server.Utiilites;

namespace Server.DatabaseAccess
{
    public static class InteractionEndpoints
    {
        public static WebApplication MapDatabaseInteractionEndpoints(this WebApplication app) 
        {
            var databaseGroup = app.MapGroup("api/v1/database").WithTags("Database Interaction");

            databaseGroup.MapPost("/execute/{name}", async (DatabaseGateManager _gateManager, HttpContext _context,[FromRoute] string name, [FromHeader] int timeout = 30) =>
            {
                //if (name.ToLower() == "app")
                //    return Results.Forbid();

                using var reader = new StreamReader(_context.Request.Body);
                string sql = await reader.ReadToEndAsync();

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

                var tokens = sql.Split(" ").ToList();

                var restricted = tokens.ContainsRestrictedTokens();
                if (restricted)
                    return Results.Conflict("Prohibited Statement");

                var exists = DirectoryManager.DatabaseFileExists(name);

                if (!exists)
                    return Results.NotFound();

                var userPermissions = _context.ExtractAllowedPermissions(name);

                if (userPermissions is null || !userPermissions.Any())
                    return Results.Forbid();

                if (!_context.HasWritePermissions(name))
                    return Results.Forbid();

                var securityRequirement = tokens.MinimalAccessRequired();

                var permissionGranted = userPermissions.MeetsPermissionRequirements(securityRequirement);

                if (!permissionGranted)
                    return Results.Forbid();


                var result = await _gateManager.ExecuteAsync(new SqlRequest { Database = name, Statement = sql, Timeout = timeout });

                return result.ToResult(true);

            })
                .RequireAuthorization()
                .Produces<long>(200)
                .Accepts<string>("application/sql")
                .WithOpenApi()
                .WithDisplayName("Execute")
                .WithName("Execute")
                .WithDescription("Execute a command to a database")
                .WithSummary("Execute");

            databaseGroup.MapPost("/query/{name}", async (DatabaseGateManager _gateManager, HttpContext _context, [FromRoute] string name, [FromHeader] int timeout = 30) =>
            {

                using var reader = new StreamReader(_context.Request.Body);
                string sql = await reader.ReadToEndAsync();

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

                var tokens = sql.Split(" ").ToList();

                var restricted = tokens.ContainsRestrictedTokens();
                if (restricted)
                    return Results.Conflict("Prohibited Statement");

                var exists = DirectoryManager.DatabaseFileExists(name);

                if (!exists)
                    return Results.NotFound();

                var userPermissions = _context.ExtractAllowedPermissions(name);

                if (userPermissions is null || !userPermissions.Any())
                    return Results.Forbid();

                var result = await _gateManager.QueryAsync(new SqlRequest { Database = name, Statement = sql, Timeout = timeout });


                if (result.Success)
                {
                    if (string.IsNullOrEmpty(result.Message))
                        return result.ToResult();
                    else
                        return Results.Json(result.Data, statusCode: 206);
                }
                return result.ToResult();
            })
                .RequireAuthorization()
                .Produces<long>(200)
                .Accepts<string>("application/sql")
                .WithOpenApi()
                .WithDisplayName("Query")
                .WithName("Query")
                .WithDescription("Query a database")
                .WithSummary("Query");

            return app;
        }
    }
}
