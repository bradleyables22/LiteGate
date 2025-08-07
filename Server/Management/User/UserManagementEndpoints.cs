using Microsoft.AspNetCore.Mvc;
using Server.Authentication;
using Server.Services;
using Server.Utiilites;

namespace Server.Management.User
{
    public static class UserManagementEndpoints
    {
        public static WebApplication MapUserManagementEndpoints(this WebApplication app) 
        {
            var userGroup = app.MapGroup("api/v1/users").WithTags("User Management");

            userGroup.MapGet("", async (UserDatabase _db, long skip = 0, int take = 10) =>
            {
                var usersResult = await _db.GetUsersAsync(skip, take);

                return usersResult.ToResult();
            })
            .RequireAuthorization(policy => policy.RequireRole("*:admin", "*:owner", "*:editor", "*:viewer", "app.db:admin", "app.db:owner", "app.db:editor", "app.db:viewer"))
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
            .RequireAuthorization(policy => policy.RequireRole("*:admin", "*:owner", "*:editor","*:viewer", "app.db:admin", "app.db:owner", "app.db:editor","app.db:viewer"))
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
            .RequireAuthorization(policy => policy.RequireRole("*:admin", "*:owner","*:editor", "app.db:admin", "app.db:owner", "app.db:editor"))
            .Produces<AppUser>()
            .Accepts<UserCredentials>("application/json")
            .WithOpenApi()
            .WithDisplayName("CreateUser")
            .WithName("CreateUser")
            .WithDescription("Create a new user")
            .WithSummary("Create User")
            ;
            
            userGroup.MapPut("/Password", async ([FromBody] PasswordChangeRequest passwordRequest, UserDatabase _db, HttpContext _ctx) =>
            {
                var currentUserId = _ctx.User.FindFirst("id")?.Value;
                if (string.IsNullOrEmpty(currentUserId))
                    return Results.Forbid();

                var changeResult = await _db.ChangePasswordAsync(currentUserId,passwordRequest.Password);

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

            userGroup.MapDelete("/{id}", async (string id, UserDatabase _db, HttpContext _ctx) =>
            {
                var currentUserId = _ctx.User.FindFirst("id")?.Value;

                if (currentUserId == id)
                    return Results.Forbid();

                var deleteResult = await _db.DeleteUserByIdAsync(id);
                return deleteResult.ToResult();
            })
            .RequireAuthorization(policy => policy.RequireRole("*:admin", "*:owner", "app.db:admin", "app.db:owner"))
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
