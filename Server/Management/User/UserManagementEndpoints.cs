using Microsoft.AspNetCore.Mvc;
using Server.Authentication.Models;
using Server.Services;
using Server.Utilities;

namespace Server.Management.User
{
    public static class UserManagementEndpoints
    {
        public static WebApplication MapUserManagementEndpoints(this WebApplication app) 
        {
            var userGroup = app.MapGroup("api/v1/usermanagement").WithTags("User Management");

            userGroup.MapGet("", async (UserDatabase _db, long skip = 0, int take = 10) =>
            {
                var usersResult = await _db.GetUsersByOffsetAsync(skip, take);

                return usersResult.ToResult();
            })
            .RequireAuthorization(policy => policy.RequireRole("*:admin", "*:editor", "*:viewer", "app:admin", "app:editor", "app:viewer"))
            .Produces(200)
            .Accepts<OffsetTryResult<AppUser>>("application/json")
            .WithOpenApi()
            .WithDisplayName("GetUsers")
            .WithName("GetUsers")
            .WithDescription("Get users by offset.")
            .WithSummary("Get Users")
            ;

            userGroup.MapGet("/{name}", async (UserDatabase _db, string name) =>
            {
                var usersResult = await _db.GetUserByNameAsync(name);

                return usersResult.ToResult();
            })
            .RequireAuthorization(policy => policy.RequireRole("*:admin", "*:editor","*:viewer", "app:admin", "app:editor","app:viewer"))
            .Produces(200)
            .Accepts<AppUser>("application/json")
            .WithOpenApi()
            .WithDisplayName("GetUser")
            .WithName("GetUser")
            .WithDescription("Get user")
            .WithSummary("Get User")
            ;

            userGroup.MapPost("/Create", async ([FromBody] UserCredentials credentials, UserDatabase _db) =>
            {
                var newUser = new AppUser{UserName = credentials.UserName};

                var createUserResult = await _db.CreateUserAsync(newUser, credentials.Password);

                if (createUserResult.Success)
                {
                    var userResult = await _db.GetUserByNameAsync(newUser.UserName);

                    return userResult.ToResult();
                }
                else
                    return createUserResult.ToResult();
            })
            .RequireAuthorization(policy => policy.RequireRole("*:admin", "app:admin"))
            .Produces<AppUser>()
            .Accepts<UserCredentials>("application/json")
            .WithOpenApi()
            .WithDisplayName("CreateUser")
            .WithName("CreateUser")
            .WithDescription("Create a new user")
            .WithSummary("Create User")
            ;
            
            userGroup.MapPatch("/Password", async ([FromBody] PasswordChangeRequest passwordRequest, UserDatabase _db, HttpContext _ctx) =>
            {
                var currentUserId = _ctx.User.FindFirst("id")?.Value;
                if (string.IsNullOrEmpty(currentUserId))
                    return Results.Forbid();

                var changeResult = await _db.ChangePasswordAsync(currentUserId,passwordRequest.Password);
                if (changeResult.Success)
                {
                    if (changeResult.Data)
                        return Results.Ok();
                    else
                        return Results.Problem(
                                    detail: "Could not change password",
                                    title: "Failed to update",
                                    statusCode: StatusCodes.Status500InternalServerError
                                );
                }
                else
                    return changeResult.ToResult(); 
            })
            .RequireAuthorization()
            .Produces<AppUser>()
            .Accepts<UserCredentials>("application/json")
            .WithOpenApi()
            .WithDisplayName("ChangePassword")
            .WithName("ChangePassword")
            .WithDescription("Change your password")
            .WithSummary("Change Password")
            ;

            userGroup.MapPatch("/disable/{userId}", async (string userId, UserDatabase _db, HttpContext _ctx) =>
            {
                var currentUserId = _ctx.User.FindFirst("id")?.Value;

                if (currentUserId == userId)
                    return Results.Forbid();

                var disabledResult = await _db.DisableUserAsync(userId);

                if (disabledResult.Success)
                {
                    var userResult = await _db.GetUserByIdAsync(userId);

                    return userResult.ToResult();
                }
                else
                    return disabledResult.ToResult();

            })
            .RequireAuthorization(policy => policy.RequireRole("*:admin", "app:admin"))
            .Produces<AppUser>()
            .WithOpenApi()
            .WithDisplayName("DisableUser")
            .WithName("DisableUser")
            .WithDescription("Disable a user")
            .WithSummary("Disable User")
            ;

            userGroup.MapPatch("/enable/{userId}", async (string userId, UserDatabase _db, HttpContext _ctx) =>
            {
                var currentUserId = _ctx.User.FindFirst("id")?.Value;

                if (currentUserId == userId)
                    return Results.Forbid();

                var enabledResult = await _db.EnableUserAsync(userId);

                if (enabledResult.Success)
                {
                    var userResult = await _db.GetUserByIdAsync(userId);

                    return userResult.ToResult();
                }
                else
                    return enabledResult.ToResult();

            })
            .RequireAuthorization(policy => policy.RequireRole("*:admin", "app:admin"))
            .Produces<AppUser>()
            .WithOpenApi()
            .WithDisplayName("enableUser")
            .WithName("enableUser")
            .WithDescription("Enable a user")
            .WithSummary("Enable User")
            ;

            userGroup.MapDelete("/{id}", async (string id, UserDatabase _db, HttpContext _ctx) =>
            {
                var currentUserId = _ctx.User.FindFirst("id")?.Value;

                if (currentUserId == id)
                    return Results.Forbid();

                var deleteResult = await _db.DeleteUserByIdAsync(id);
                if (deleteResult.Success) 
                {
                    if (deleteResult.Data)
                        return Results.Ok();
                    else
                        return Results.Problem(
                                    detail: "Could not delete user",
                                    title: "Failed to delete",
                                    statusCode: StatusCodes.Status500InternalServerError
                                );
                }
                else
                    return deleteResult.ToResult();
            })
            .RequireAuthorization(policy => policy.RequireRole("*:admin", "app:admin"))
            .Produces(200)
            .WithOpenApi()
            .WithDisplayName("DeleteUser")
            .WithName("DeleteUser")
            .WithDescription("Delete a user")
            .WithSummary("Delete User")
            ;

            return app;
        }
    }
}
