using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Server.Authentication;
using Server.Services;
using Server.Utiilites;
using System.Data;

namespace Server.Management.User
{
    public static class RoleManagementEndpoints
    {
        public static WebApplication MapUserRoleManagementEndpoints(this WebApplication app)
        {
            var roleGroup = app.MapGroup("api/v1/rolemanagement").WithTags("Role Management");

            roleGroup.MapGet("/available", (UserDatabase _db) =>
            {
                var roles = Enum.GetValues<SystemRole>()
                    .Select(role => new RoleDefinition(
                        Id: role,
                        Name: System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(role.ToString())
                    ));

                return Results.Ok(roles);
            })
            .RequireAuthorization(policy => policy.RequireRole("*:admin", "*:editor", "*:viewer", "app.db:admin", "app.db:editor", "app.db:viewer"))
            .Produces<List<RoleDefinition>>()
            .WithOpenApi()
            .WithDisplayName("GetRoles")
            .WithName("GetRoles")
            .WithDescription("Get all available roles")
            .WithSummary("Get Roles")
            ;

            roleGroup.MapPut("/overwrite/{userId}", async ([FromBody] List<DatabaseRole> roles,string userId, UserDatabase _db) =>
            {
                var userResult = await _db.GetUserByIdAsync(userId);

                if (!userResult.Success)
                    return userResult.ToResult();

                if (userResult.Data is null)
                    return Results.NotFound();

                foreach (var role in roles)
                {
                    if (role.Database != "*")
                    {
                        if (!role.Database.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                            role.Database += ".db";

                        var exists = DirectoryManager.DatabaseFileExists(role.Database);
                        if (!exists)
                            return Results.BadRequest($"{role.Database} does not exist");
                    }
                }

                var result = await _db.UpdateUserRolesAsync(userId, roles);

                if (result.Success && result.Data == true) 
                {
                    var latest = await _db.GetUserByIdAsync(userId);

                    return latest.ToResult();   
                }

                return result.ToResult();
            })
            .RequireAuthorization(policy => policy.RequireRole("*:admin", "app.db:admin"))
            .Produces<AppUser>()
            .Accepts<UserCredentials>("application/json")
            .WithOpenApi()
            .WithDisplayName("OverwriteUserRoles")
            .WithName("OverwriteUserRoles")
            .WithDescription("Overwrite a users roles")
            .WithSummary("Overwrite User Roles")
            ;

            roleGroup.MapPatch("/add/{userId}", async ([FromBody] DatabaseRole role,string userId, UserDatabase _db, HttpContext _ctx) =>
            {
               var userResult = await _db.GetUserByIdAsync(userId);

                if (!userResult.Success)
                    return userResult.ToResult();

                if (userResult.Data is null)
                    return Results.NotFound();

                if (userResult.Data.Roles is null)
                    userResult.Data.Roles = new();

                if (role.Database != "*")
                {
                    var databaseExists = DirectoryManager.DatabaseFileExists(role.Database);
                    if (!databaseExists)
                        return Results.BadRequest("Database doesn't exist");

                    if (!role.Database.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                        role.Database += ".db";
                }

                var existingRole = userResult.Data.Roles.Where(x => x.Database == role.Database && x.Role == role.Role).FirstOrDefault();

                if (existingRole is null)
                    userResult.Data.Roles.Add(role);

                var result = await _db.UpdateUserRolesAsync(userId, userResult.Data.Roles);

                if (result.Success && result.Data == true)
                    return Results.Ok(userResult.Data);

                return result.ToResult();

            })
            .RequireAuthorization(policy => policy.RequireRole("*:admin", "app.db:admin"))
            .Produces<AppUser>()
            .WithOpenApi()
            .WithDisplayName("AddUserRole")
            .WithName("AddUserRole")
            .WithDescription("Add a new role to a user")
            .WithSummary("Add User Role")
            ;


            roleGroup.MapPatch("/remove/{userId}", async ([FromBody] DatabaseRole role, string userId, UserDatabase _db, HttpContext _ctx) =>
            {
                var userResult = await _db.GetUserByIdAsync(userId);

                if (!userResult.Success)
                    return userResult.ToResult();

                if (userResult.Data is null)
                    return Results.NotFound();

                if (userResult.Data.Roles is null)
                    userResult.Data.Roles = new();

                var existingRole = userResult.Data.Roles.Where(x => x.Database == role.Database && x.Role == role.Role).FirstOrDefault();

                if (existingRole is null)
                    return Results.NotFound();

                userResult.Data.Roles.Remove(existingRole);

                var result = await _db.UpdateUserRolesAsync(userId, userResult.Data.Roles);

                if (result.Success && result.Data == true) 
                {
                    var latest = await _db.GetUserByIdAsync(userId);
                    return latest.ToResult();
                }

                return result.ToResult();

            })
           .RequireAuthorization(policy => policy.RequireRole("*:admin", "app.db:admin"))
           .Produces<AppUser>()
           .WithOpenApi()
           .WithDisplayName("RemoveUserRole")
           .WithName("RemoveUserRole")
           .WithDescription("Remove a role from a user")
           .WithSummary("Remove User Role")
           ;

            roleGroup.MapDelete("/clear/{userId}", async (string userId, UserDatabase _db, HttpContext _ctx) =>
            {
                var userResult = await _db.GetUserByIdAsync(userId);

                if (!userResult.Success)
                    return userResult.ToResult();

                if (userResult.Data is null)
                    return Results.NotFound();

                if (userResult.Data.Roles is null || userResult.Data.Roles.Any())
                    userResult.Data.Roles = new();

                var result = await _db.UpdateUserRolesAsync(userId, userResult.Data.Roles);
                if (result.Success && result.Data == true)
                {
                    var latest = await _db.GetUserByIdAsync(userId);
                    return latest.ToResult();
                }

                return result.ToResult();
            })
           .RequireAuthorization(policy => policy.RequireRole("*:admin", "app.db:admin"))
           .Produces<AppUser>()
           .WithOpenApi()
           .WithDisplayName("ClearUserRoles")
           .WithName("ClearUserRoles")
           .WithDescription("Clear all roles from a user")
           .WithSummary("Clear User Roles")
           ;

            return app;
        }
    }
}
